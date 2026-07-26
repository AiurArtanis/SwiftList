using System.Collections.Concurrent;
using SwiftList.Core.Indexer.NetworkDrive.Walk;

namespace SwiftList.Core.Indexer.Usn.Journal;

// Mid-walk checkpoint publishing for ReFsScanner, split into its own file (matching
// TreeBuilderCheckpointExtensions' own split off TreeBuilder) to keep ReFsScanner.cs under the project's
// line limit. ReFsScanner has no long-lived instance the way TreeBuilder does, so CheckpointState bundles
// what MaybeCheckpoint needs across every worker task's calls -- the constant identity fields a checkpoint
// store needs (drive/rootFrn/nextUsn/journalId), the callback itself, and the same Interlocked-guarded
// doubling-interval counters TreeBuilderCheckpointExtensions.MaybeCheckpoint uses, reusing TreeBuilder's
// own CheckpointBatchSize/MaxCheckpointBatchSize constants for identical write-volume behavior.
internal sealed class ReFsCheckpointState
{
    public readonly string Drive;
    public readonly UInt128 RootFrn;
    public readonly long NextUsn;
    public readonly ulong JournalId;
    public readonly Action<FileRecordStore, NetworkDriveWalkStats>? OnCheckpoint;
    public int BatchSize = TreeBuilder.CheckpointBatchSize;
    public int CountSinceCheckpoint;
    public int CheckpointInFlight;

    public ReFsCheckpointState(string drive, UInt128 rootFrn, long nextUsn, ulong journalId, Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint)
    {
        Drive = drive;
        RootFrn = rootFrn;
        NextUsn = nextUsn;
        JournalId = journalId;
        OnCheckpoint = onCheckpoint;
    }
}

internal static class ReFsScannerCheckpointExtensions
{
    public static void MaybeCheckpoint(this ReFsCheckpointState? state, ConcurrentDictionary<UInt128, ReFsItem> items)
    {
        if (state?.OnCheckpoint == null)
            return;

        var threshold = Volatile.Read(ref state.BatchSize);
        var count = Interlocked.Increment(ref state.CountSinceCheckpoint);
        if (count < threshold)
            return;

        // Guards against multiple worker tasks crossing the threshold at once: only the one whose reset
        // actually finds a nonzero counter proceeds, so exactly one checkpoint fires per threshold crossing
        // -- same reasoning as TreeBuilderCheckpointExtensions.MaybeCheckpoint.
        if (Interlocked.Exchange(ref state.CountSinceCheckpoint, 0) == 0)
            return;

        // On a mostly-reused resume, threshold crossings can come faster than a checkpoint's own disk
        // write finishes -- skipping (not blocking) is safe: nothing is lost, whatever would've gone into
        // this checkpoint just rides along in the next one that actually runs.
        if (Interlocked.CompareExchange(ref state.CheckpointInFlight, 1, 0) != 0)
            return;

        try
        {
            // Point-in-time copy -- ConcurrentDictionary's own enumeration is thread-safe against
            // concurrent writes from other workers, but IndexCacheManager.CreateStoreFromDriveData needs a
            // plain Dictionary; a checkpoint's contents are inherently a partial, ephemeral snapshot
            // regardless of exactly which in-flight writes did or didn't make it in.
            var snapshot = new Dictionary<UInt128, ReFsItem>(items);
            var store = IndexCacheManager.CreateStoreFromDriveData(state.Drive, state.RootFrn, snapshot, state.NextUsn, state.JournalId);
            state.OnCheckpoint(store, default);
            // Double the gap before the NEXT checkpoint (capped) -- see TreeBuilder.MaxCheckpointBatchSize's
            // own comment for why a flat interval is O(n^2) total write volume on a full rebuild.
            Volatile.Write(ref state.BatchSize, Math.Min(threshold * 2, TreeBuilder.MaxCheckpointBatchSize));
        }
        finally
        {
            Interlocked.Exchange(ref state.CheckpointInFlight, 0);
        }
    }
}
