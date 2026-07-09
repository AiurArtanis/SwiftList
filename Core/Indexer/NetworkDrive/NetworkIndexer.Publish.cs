namespace SwiftList.Core.Indexer.NetworkDrive;

// Status/index publishing half of NetworkIndexer, split out to keep NetworkIndexer.cs under the project's
// line limit.
public sealed partial class NetworkIndexer
{
    private void SetStatus(string drive, string state, int? items, string? error)
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
    private FileRecordStore? GetPreviousStore(string drive)
    {
        lock (_gate)
            return _indexes.TryGetValue(drive, out var index) ? index.ToStore() : null;
    }

    private void OnRefreshFinished(string drive, NetworkIndex index)
    {
        lock (_gate)
        {
            _indexes[drive] = index;
            _statuses[drive] = NetworkIndexerHelper.CreateStatus(drive, "ready", index.Count, index, null);
        }
        _watcherManager?.EnsureWatcher(drive);
        PublishStatusesChanged();
    }

    private void PublishIncrementalUpdate(string drive, NetworkIndex index)
    {
        IndexerHelper.Save(index);
        lock (_gate)
        {
            _statuses.TryGetValue(drive, out var current);
            _statuses[drive] = NetworkIndexerHelper.CreateStatus(drive, "ready", index.Count, index, current);
        }
        PublishStatusesChanged();
    }

    private void PublishCheckpoint(string drive, FileRecordStore store, NetworkDriveWalkStats stats, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        try
        {
            var index = NetworkIndex.FromStore(store, stats);

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
                    // "indexing" a moment after the user stopped it.
                    if (token.IsCancellationRequested)
                        return;
                    _indexes.TryGetValue(drive, out var current);
                    _statuses[drive] = NetworkIndexerHelper.CreateStatus(drive, "indexing", index.Count, current, null);
                }
            }
            else
            {
                IndexerHelper.Save(index);
                lock (_gate)
                {
                    if (token.IsCancellationRequested)
                        return;
                    _indexes[drive] = index;
                    _statuses[drive] = NetworkIndexerHelper.CreateStatus(drive, "indexing", index.Count, index, null);
                }
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
    }

    private void PublishStatusesChanged()
    {
        try
        {
            StatusesChanged?.Invoke(GetStatuses());
        }
        catch
        {
        }
    }
}
