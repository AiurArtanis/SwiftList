namespace SwiftList.Core.Indexer.NetworkDrive.Walk;

// Mid-walk snapshotting for TreeBuilder, as extension methods (matching RuntimeIndex's BucketExtensions/
// QueryExtensions split) instead of a partial class, to keep TreeBuilder.cs under the project's line
// limit. Strictly count-based -- no wall-clock fallback, so a checkpoint only ever fires once that many
// items have genuinely been processed since the last one. The gap itself grows (see
// TreeBuilder._checkpointBatchSize) rather than staying fixed at CheckpointBatchSize forever.
internal static class TreeBuilderCheckpointExtensions
{
    public static void MaybeCheckpoint(this TreeBuilder builder, int indexedItems)
    {
        if (builder._onCheckpoint == null)
            return;

        var threshold = Volatile.Read(ref builder._checkpointBatchSize);
        var count = Interlocked.Increment(ref builder._countSinceCheckpoint);
        if (count < threshold)
            return;

        // Guards against multiple threads crossing the threshold at once: only the one whose reset actually
        // finds a nonzero counter proceeds, so exactly one checkpoint fires per threshold crossing.
        if (Interlocked.Exchange(ref builder._countSinceCheckpoint, 0) == 0)
            return;

        // The reuse-copy path has no network I/O throttling it, so on a mostly-cached resume threshold
        // crossings can come faster than a checkpoint's own disk write finishes. Without this, a second
        // checkpoint's save can start on the same cache files before the first one's temp-file swap is
        // done -- exactly the concurrent-write collisions IndexerHelper.Save was logging. Skipping (not
        // blocking) is safe: nothing is lost, the items that would've gone into this checkpoint just ride
        // along in the next one that actually gets to run.
        if (Interlocked.CompareExchange(ref builder._checkpointInFlight, 1, 0) != 0)
            return;

        try
        {
            builder._onProgress(indexedItems);
            builder._onCheckpoint(CloneStore(builder), CurrentStats(builder));
            // Double the gap before the NEXT checkpoint (capped) -- see TreeBuilder.MaxCheckpointBatchSize's
            // own comment for why a flat interval is O(n^2) total write volume on a full rebuild.
            Volatile.Write(ref builder._checkpointBatchSize, Math.Min(threshold * 2, TreeBuilder.MaxCheckpointBatchSize));
        }
        finally
        {
            Interlocked.Exchange(ref builder._checkpointInFlight, 0);
        }
    }

    private static FileRecordStore CloneStore(TreeBuilder builder)
    {
        lock (builder._recordsGate)
        {
            var clone = new FileRecordStore
            {
                SourceKey = builder._store.SourceKey,
                SourceKind = builder._store.SourceKind,
                IdKind = builder._store.IdKind,
                RootId = builder._store.RootId,
                JournalId = builder._store.JournalId,
                NextUsn = builder._store.NextUsn,
                // Without this, a checkpoint saved mid-walk would look fingerprint-less on the next resume,
                // forcing an unnecessary (but harmless) recheck pass instead of correctly recognizing that
                // exclusion rules haven't actually changed since this in-progress scan started.
                ExclusionRulesFingerprint = builder._store.ExclusionRulesFingerprint
            };
            clone.Records.AddRange(builder._store.Records);
            return clone;
        }
    }

    private static NetworkDriveWalkStats CurrentStats(TreeBuilder builder) => new NetworkDriveWalkStats(
            Volatile.Read(ref builder._skippedItems),
            Volatile.Read(ref builder._errors),
            Volatile.Read(ref builder._enumerateErrors),
            Volatile.Read(ref builder._attributeErrors),
            Volatile.Read(ref builder._reparseSkipped),
            Volatile.Read(ref builder._slowDirectories));
}
