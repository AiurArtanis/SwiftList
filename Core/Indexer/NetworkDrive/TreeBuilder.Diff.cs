namespace SwiftList.Core.Indexer.NetworkDrive;

// Diff-aware half of TreeBuilder: reusing a directory's cached children instead of re-listing it over
// the network when TreeDiffBaseline confirms nothing changed, and tracking which directories in THIS
// store have been fully enumerated (FileRecordFlags.Listed) so a future resume can trust them the same
// way. Split into its own file to keep TreeBuilder.cs under the project's line limit.
internal sealed partial class TreeBuilder
{
    private readonly TreeDiffBaseline? _diffBaseline;
    // True when the exclusion rules fingerprint on the previous store doesn't match the current one --
    // see NetworkIndex.Build. A reused (mtime-unchanged) directory's cached children were filtered under
    // whatever rules were active *then*; a path just un-excluded since would never surface without this.
    private readonly bool _recheckExclusions;
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

        Interlocked.Increment(ref _reusedDirectories);

        var ignoreRules = _filter.LoadIgnoreRules(current.Path, current.LogicalPath, current.IgnoreRules);
        var batch = new List<FileRecord>(RecordBatchSize);
        // Only populated when a recheck is actually needed -- ReconcileLiveEntries below uses it to tell
        // "already accounted for from cache" apart from "new to us", without a second full pass over
        // previousChildren.
        var previousByName = _recheckExclusions ? new Dictionary<string, FileRecord>(StringComparer.OrdinalIgnoreCase) : null;

        foreach (var child in previousChildren)
        {
            _token.ThrowIfCancellationRequested();
            previousByName?.TryAdd(child.Name, child);

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

        if (previousByName != null)
            ReconcileLiveEntries(current, ignoreRules, previousByName, batch);

        FlushRecords(batch);
        MarkListed(current.LocalId);
        return true;
    }

    // Only reached when exclusion rules may have changed since this directory was last fully listed (see
    // _recheckExclusions). Lists the directory once -- one round trip, cheap next to a per-item stat -- and
    // processes only the names the cache-driven pass above didn't already account for: something the old
    // rules excluded and the new ones don't (or, defensively, a name that's genuinely new despite the
    // parent's unchanged mtime -- shouldn't happen if mtime is reliable, but costs nothing extra to allow
    // for). Anything cached that no longer shows up here just silently isn't re-added to batch -- the
    // deletion side of the same coin, self-correcting even if mtime turns out unreliable on some filesystem.
    private void ReconcileLiveEntries(WorkItem current, NetworkIgnoreRuleSet ignoreRules, Dictionary<string, FileRecord> previousByName, List<FileRecord> batch)
    {
        IEnumerable<string> liveEntries;
        try
        {
            liveEntries = Directory.EnumerateFileSystemEntries(current.Path);
        }
        catch
        {
            CountError(ref _enumerateErrors);
            return;
        }

        foreach (var entry in liveEntries)
        {
            _token.ThrowIfCancellationRequested();

            var name = Path.GetFileName(entry.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name) || previousByName.ContainsKey(name))
                continue;

            var createResult = TryCreateRecord(entry, current.LogicalPath, current.LocalId, out var record, out var isDirectory, out var logicalFullPath);
            if (createResult != WalkRecordResult.Success)
            {
                CountCreateFailure(createResult);
                continue;
            }

            if (!_filter.ShouldIndex(logicalFullPath, record.Name, isDirectory, record.Attributes, ignoreRules))
            {
                Interlocked.Increment(ref _skippedItems);
                continue;
            }

            batch.Add(record);
            if (batch.Count >= RecordBatchSize)
                FlushRecords(batch);

            var indexedItems = Interlocked.Increment(ref _indexedItems);

            if (isDirectory && _filter.ShouldDescend(logicalFullPath, record.Attributes, current.Depth + 1, ignoreRules))
            {
                FlushRecords(batch);
                EnqueueDirectory(entry, logicalFullPath, record.Id, current.Depth + 1, ignoreRules);
            }

            if (Interlocked.Increment(ref _countSinceProgress) >= ProgressBatchSize)
            {
                Interlocked.Exchange(ref _countSinceProgress, 0);
                _onProgress(indexedItems);
            }

            MaybeCheckpoint(indexedItems);
        }
    }
}
