namespace SwiftList.Core.Indexer.NetworkDrive;

// Mid-walk snapshotting, split out of TreeBuilder.cs to keep it under the project's line limit. Strictly
// count-based (every CheckpointBatchSize items) -- no wall-clock fallback, so a checkpoint only ever fires
// once that many items have genuinely been processed since the last one.
internal sealed partial class TreeBuilder
{
    private void MaybeCheckpoint(int indexedItems)
    {
        if (_onCheckpoint == null)
            return;

        var count = Interlocked.Increment(ref _countSinceCheckpoint);
        if (count < CheckpointBatchSize)
            return;

        // Guards against multiple threads crossing the threshold at once: only the one whose reset actually
        // finds a nonzero counter proceeds, so exactly one checkpoint fires per CheckpointBatchSize items.
        if (Interlocked.Exchange(ref _countSinceCheckpoint, 0) == 0)
            return;

        _onProgress(indexedItems);
        _onCheckpoint(CloneStore(), CurrentStats());
    }

    private FileRecordStore CloneStore()
    {
        lock (_recordsGate)
        {
            var clone = new FileRecordStore
            {
                SourceKey = _store.SourceKey,
                SourceKind = _store.SourceKind,
                IdKind = _store.IdKind,
                RootId = _store.RootId,
                JournalId = _store.JournalId,
                NextUsn = _store.NextUsn
            };
            clone.Records.AddRange(_store.Records);
            return clone;
        }
    }

    private NetworkDriveWalkStats CurrentStats() => new NetworkDriveWalkStats(
            Volatile.Read(ref _skippedItems),
            Volatile.Read(ref _errors),
            Volatile.Read(ref _enumerateErrors),
            Volatile.Read(ref _attributeErrors),
            Volatile.Read(ref _reparseSkipped),
            Volatile.Read(ref _slowDirectories));
}
