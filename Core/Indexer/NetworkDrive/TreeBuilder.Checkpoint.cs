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

        // The reuse-copy path has no network I/O throttling it, so on a mostly-cached resume threshold
        // crossings can come faster than a checkpoint's own disk write finishes. Without this, a second
        // checkpoint's save can start on the same cache files before the first one's temp-file swap is
        // done -- exactly the concurrent-write collisions IndexerHelper.Save was logging. Skipping (not
        // blocking) is safe: nothing is lost, the items that would've gone into this checkpoint just ride
        // along in the next one that actually gets to run.
        if (Interlocked.CompareExchange(ref _checkpointInFlight, 1, 0) != 0)
            return;

        try
        {
            _onProgress(indexedItems);
            _onCheckpoint(CloneStore(), CurrentStats());
        }
        finally
        {
            Interlocked.Exchange(ref _checkpointInFlight, 0);
        }
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
                NextUsn = _store.NextUsn,
                // Without this, a checkpoint saved mid-walk would look fingerprint-less on the next resume,
                // forcing an unnecessary (but harmless) recheck pass instead of correctly recognizing that
                // exclusion rules haven't actually changed since this in-progress scan started.
                ExclusionRulesFingerprint = _store.ExclusionRulesFingerprint
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
