namespace SwiftList.Core.Indexer.NetworkDrive;

// Diff-aware half of TreeBuilder: reusing a directory's cached children instead of re-listing it over
// the network when TreeDiffBaseline confirms nothing changed, and tracking which directories in THIS
// store have been fully enumerated (FileRecordFlags.Listed) so a future resume can trust them the same
// way. Split into its own file to keep TreeBuilder.cs under the project's line limit.
internal sealed partial class TreeBuilder
{
    private readonly TreeDiffBaseline? _diffBaseline;
    private readonly Dictionary<UInt128, int> _indexById = new();

    // Must be called with _recordsGate already held (both call sites -- the constructor seeding any
    // pre-existing records, and FlushRecords -- already hold it or run before workers start).
    private void RegisterDirectoryIndices(int startIndex, List<FileRecord> records)
    {
        for (var i = 0; i < records.Count; i++)
        {
            if (records[i].IsDirectory)
                _indexById[records[i].Id] = startIndex + i;
        }
    }

    private void MarkListed(UInt128 id)
    {
        lock (_recordsGate)
        {
            if (!_indexById.TryGetValue(id, out var index))
                return;

            var r = _store.Records[index];
            if ((r.Flags & FileRecordFlags.Listed) != 0)
                return;

            _store.Records[index] = new FileRecord(
                r.Id, r.ParentId, r.Name, r.Flags | FileRecordFlags.Listed,
                r.Size, r.CreationTimeUnixSeconds, r.LastWriteTimeUnixSeconds, r.LastAccessTimeUnixSeconds);
        }
    }

    // Reuses current's previously-recorded children wholesale instead of listing the directory again, when
    // TreeDiffBaseline confirms it was fully captured last time and hasn't changed since. Still recurses
    // into every cached child directory individually -- a directory's own LastWriteTime only reflects its
    // direct children, never anything deeper -- so this only ever skips ONE level of listing per call, not
    // an entire subtree at once.
    private bool TryReuseUnchangedDirectory(WorkItem current)
    {
        if (!_diffBaseline!.TryGetUnchangedChildren(current.Path, current.LocalId, out var previousChildren))
            return false;

        var ignoreRules = _filter.LoadIgnoreRules(current.Path, current.LogicalPath, current.IgnoreRules);
        var batch = new List<FileRecord>(RecordBatchSize);

        foreach (var child in previousChildren)
        {
            _token.ThrowIfCancellationRequested();

            var isDirectory = child.IsDirectory;
            var attributes = FileRecordFlagsHelper.ToAttributes(child.Flags);
            var logicalFullPath = PathHelpers.NormalizePath(Path.Combine(current.LogicalPath, child.Name), isDirectory);

            if (!_filter.ShouldIndex(logicalFullPath, child.Name, isDirectory, attributes, ignoreRules))
            {
                Interlocked.Increment(ref _skippedItems);
                continue;
            }

            batch.Add(child);
            if (batch.Count >= RecordBatchSize)
                FlushRecords(batch);

            var indexedItems = Interlocked.Increment(ref _indexedItems);

            if (isDirectory && _filter.ShouldDescend(logicalFullPath, attributes, current.Depth + 1, ignoreRules))
            {
                // Same ordering requirement as WalkDirectory's fresh-listing path: flush before enqueueing
                // so this child's own record is in _indexById before another worker can dequeue it.
                FlushRecords(batch);
                var physicalChildPath = Path.Combine(current.Path, child.Name);
                EnqueueDirectory(physicalChildPath, logicalFullPath, child.Id, current.Depth + 1, ignoreRules);
            }

            if (Interlocked.Increment(ref _countSinceProgress) >= ProgressBatchSize)
            {
                Interlocked.Exchange(ref _countSinceProgress, 0);
                _onProgress(indexedItems);
            }

            MaybeCheckpoint(indexedItems);
        }

        FlushRecords(batch);
        MarkListed(current.LocalId);
        return true;
    }
}
