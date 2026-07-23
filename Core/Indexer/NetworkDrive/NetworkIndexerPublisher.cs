using SwiftList.Core.Indexer.NetworkDrive.Walk;
namespace SwiftList.Core.Indexer.NetworkDrive;

// Status/index publishing for NetworkIndexer -- extracted into its own class (composition, not a
// partial class) to keep NetworkIndexer.cs under the project's line limit. Shares NetworkIndexer's own
// _gate/_statuses/_indexes dictionaries by reference rather than owning copies, since both types need
// to observe the same live state.
internal sealed class NetworkIndexerPublisher
{
    private readonly object _gate;
    private readonly Dictionary<string, NetworkIndexStatus> _statuses;
    private readonly Dictionary<string, NetworkIndex> _indexes;
    private readonly Action<string> _ensureWatcher;
    private readonly Func<IReadOnlyList<NetworkIndexStatus>> _getStatuses;
    private readonly Action<IReadOnlyList<NetworkIndexStatus>> _raiseStatusesChanged;

    public NetworkIndexerPublisher(
        object gate,
        Dictionary<string, NetworkIndexStatus> statuses,
        Dictionary<string, NetworkIndex> indexes,
        Action<string> ensureWatcher,
        Func<IReadOnlyList<NetworkIndexStatus>> getStatuses,
        Action<IReadOnlyList<NetworkIndexStatus>> raiseStatusesChanged)
    {
        _gate = gate;
        _statuses = statuses;
        _indexes = indexes;
        _ensureWatcher = ensureWatcher;
        _getStatuses = getStatuses;
        _raiseStatusesChanged = raiseStatusesChanged;
    }

    public void SetStatus(string drive, string state, int? items, string? error)
    {
        lock (_gate)
        {
            // A scan already in flight when its drive got removed from config (Configure() deletes the
            // entry synchronously) keeps running cooperatively for a bit until its token check trips --
            // any status/progress callback it fires in that window must not resurrect an entry for a
            // drive the user just disabled.
            if (!_statuses.TryGetValue(drive, out var current))
                return;
            _statuses[drive] = NetworkIndexerHelper.CreateStatus(
                drive, state, items ?? current.Items, null, current, error ?? string.Empty);
        }
        PublishStatusesChanged();
    }

    // Whatever's currently loaded for this drive (a completed index, or an interrupted checkpoint) becomes
    // TreeBuilder's diff baseline for the refresh about to run -- see TreeDiffBaseline.
    public FileRecordStore? GetPreviousStore(string drive)
    {
        lock (_gate)
            return _indexes.TryGetValue(drive, out var index) ? index.ToStore() : null;
    }

    public void OnRefreshFinished(string drive, NetworkIndex index)
    {
        NetworkIndex? old;
        bool stillTracked;
        lock (_gate)
        {
            // Mirrors SetStatus's guard: a scan already in flight when its drive got removed from config
            // (Configure() deletes _statuses[drive] synchronously) must not resurrect it here, and must not
            // re-attach a watcher below -- that watcher is what used to keep a disabled drive refreshing
            // itself forever via file-system-change events, long after Configure() tore everything else down.
            stillTracked = _statuses.ContainsKey(drive);
            if (stillTracked)
            {
                _indexes.TryGetValue(drive, out old);
                _indexes[drive] = index;
                _statuses[drive] = NetworkIndexerHelper.CreateStatus(drive, "ready", index.Count, index, null);
            }
            else
            {
                old = null;
            }
        }
        if (!stillTracked)
        {
            index.Dispose();
            return;
        }
        // Dispose OUTSIDE the lock: LiveIndex.Dispose() takes its own write lock and can briefly block
        // on an in-flight search holding its read lock -- doing that while holding _gate would stall
        // every other drive's status/index access for no reason.
        if (old != null && !ReferenceEquals(old, index))
            old.Dispose();
        _ensureWatcher(drive);
        PublishStatusesChanged();
    }

    public void PublishIncrementalUpdate(string drive, NetworkIndex index)
    {
        IndexerHelper.Save(index);
        lock (_gate)
        {
            _statuses.TryGetValue(drive, out var current);
            _statuses[drive] = NetworkIndexerHelper.CreateStatus(drive, "ready", index.Count, index, current);
        }
        PublishStatusesChanged();
    }

    public void PublishCheckpoint(string drive, FileRecordStore store, NetworkDriveWalkStats stats, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        // `index` owns a mmap-backed LiveIndex now (unlike the old engine's plain in-memory checkpoint) --
        // every path below that doesn't end up storing it into _indexes must still Dispose it, or a
        // re-validation pass against an already-complete index (the common `alreadyComplete` case) leaks
        // one mmap per checkpoint (~every 4096 items).
        NetworkIndex? index = null;
        var stored = false;
        try
        {
            index = NetworkIndex.FromStore(store, stats);

            // A checkpoint is always a partial, in-progress snapshot (IsComplete is never true here). If
            // what's currently cached for this drive is a fully complete, trusted index, a checkpoint from
            // a resume/re-validation pass that later gets interrupted must not regress it back to a
            // smaller, partial view -- skip persisting this one (to memory and disk) entirely, so the last
            // known-good complete index keeps serving searches until a full pass actually finishes and can
            // genuinely replace it. Only the live progress count in the status updates in the meantime.
            bool alreadyComplete;
            lock (_gate)
                alreadyComplete = _indexes.TryGetValue(drive, out var currentBeforeSave) && currentBeforeSave.IsComplete;

            if (alreadyComplete)
            {
                lock (_gate)
                {
                    // Re-checked here, inside the same lock the Stop button's own status revert uses --
                    // Cancel() is synchronous and its visibility to IsCancellationRequested is immediate, so
                    // if CancelDrive's revert has already run by the time this write would happen, this is
                    // guaranteed to observe it and back off instead of clobbering "cached" back to
                    // "indexing" a moment after the user stopped it. Also backs off if the drive was removed
                    // from config entirely (mirrors OnRefreshFinished's guard) -- Configure() deletes
                    // _statuses[drive] synchronously, and this checkpoint's own cancellation token may not
                    // have tripped yet.
                    if (token.IsCancellationRequested || !_statuses.ContainsKey(drive))
                        return;
                    _indexes.TryGetValue(drive, out var current);
                    _statuses[drive] = NetworkIndexerHelper.CreateStatus(drive, "indexing", index.Count, current, null);
                }
            }
            else
            {
                IndexerHelper.Save(index);
                NetworkIndex? old = null;
                lock (_gate)
                {
                    if (token.IsCancellationRequested || !_statuses.ContainsKey(drive))
                        return;
                    _indexes.TryGetValue(drive, out old);
                    _indexes[drive] = index;
                    stored = true;
                    _statuses[drive] = NetworkIndexerHelper.CreateStatus(drive, "indexing", index.Count, index, null);
                }
                if (old != null && !ReferenceEquals(old, index))
                    old.Dispose();
            }
            PublishStatusesChanged();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkIndexer] Failed to publish checkpoint for {drive}: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            if (!stored)
                index?.Dispose();
        }
    }

    public void PublishStatusesChanged()
    {
        try
        {
            _raiseStatusesChanged(_getStatuses());
        }
        catch
        {
        }
    }
}
