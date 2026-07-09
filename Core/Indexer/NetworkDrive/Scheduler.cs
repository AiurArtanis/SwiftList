using System.Diagnostics;

namespace SwiftList.Core.Indexer.NetworkDrive;

internal sealed partial class Scheduler : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _debounceCts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _periodicCts = new(StringComparer.OrdinalIgnoreCase);
    // Present for exactly the drives currently queued-to-run or actively running -- this doubles as the
    // "is this drive busy" check (replacing what used to be a separate _refreshingDrives HashSet), since
    // a drive's entry here and its busy-ness are definitionally the same thing.
    private readonly Dictionary<string, CancellationTokenSource> _activeCts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingRefreshDrives = new(StringComparer.OrdinalIgnoreCase);
    // Cancelled only on Dispose() -- unlike the old shared _refreshCts (recreated on every StartRefresh
    // call), this never interrupts a drive that's still configured. Only a drive genuinely removed from
    // config gets its own _activeCts entry cancelled (see StartRefresh); everything else keeps running
    // undisturbed across an unrelated Configure() call (e.g. a settings Apply that only touched a
    // different drive, or an exclusions change elsewhere).
    private readonly CancellationTokenSource _lifetimeCts = new();

    private readonly Action<string, string> _onWatcherEnsure;
    private readonly Action<string> _onWatcherRemove;
    private readonly Action<string, string, int?, string?> _setStatus;
    private readonly Action<string, NetworkIndex> _onRefreshFinished;
    private readonly Action<string, FileRecordStore, NetworkDriveWalkStats, CancellationToken> _onPublishCheckpoint;
    private readonly Func<string, FileRecordStore?> _getPreviousStore;

    public Scheduler(Action<string, string> onWatcherEnsure, Action<string> onWatcherRemove, Action<string, string, int?, string?> setStatus,
        Action<string, NetworkIndex> onRefreshFinished, Action<string, FileRecordStore, NetworkDriveWalkStats, CancellationToken> onPublishCheckpoint,
        Func<string, FileRecordStore?> getPreviousStore)
    {
        _onWatcherEnsure = onWatcherEnsure; _onWatcherRemove = onWatcherRemove; _setStatus = setStatus;
        _onRefreshFinished = onRefreshFinished; _onPublishCheckpoint = onPublishCheckpoint;
        _getPreviousStore = getPreviousStore;
    }

    public void StartRefresh(
        IReadOnlyList<string> drives,
        IReadOnlyDictionary<string, string> refreshModes,
        HashSet<string>? cachedDrives = null,
        IReadOnlyDictionary<string, DateTime>? lastUpdatedTimes = null)
    {
        lock (_gate)
        {
            // Every drive with any live bookkeeping (queued/running, debouncing, or on a periodic timer)
            // that is no longer in the incoming drives list is the only thing this call actually
            // interrupts -- a drive that's still configured never gets touched here, no matter why
            // StartRefresh was called.
            var removedDrives = _periodicCts.Keys
                .Concat(_debounceCts.Keys)
                .Concat(_activeCts.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Except(drives, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var removed in removedDrives)
            {
                _onWatcherRemove(removed);
                RemovePeriodicLocked(removed);
                if (_debounceCts.Remove(removed, out var debounce))
                {
                    debounce.Cancel();
                    debounce.Dispose();
                }
                if (_activeCts.Remove(removed, out var active))
                {
                    active.Cancel();
                    active.Dispose();
                }
                _pendingRefreshDrives.Remove(removed);
            }

            foreach (var drive in drives)
            {
                var mode = refreshModes.TryGetValue(drive, out var value) ? value : "Manual";
                var lastUpdated = lastUpdatedTimes != null && lastUpdatedTimes.TryGetValue(drive, out var lu) ? lu : (DateTime?)null;
                _onWatcherEnsure(drive, mode);
                EnsurePeriodicRefreshLocked(drive, mode, lastUpdated);
            }
        }

        foreach (var drive in drives)
        {
            var mode = refreshModes.TryGetValue(drive, out var value) ? value : "Manual";
            bool alreadyActive;
            lock (_gate)
                alreadyActive = _activeCts.ContainsKey(drive);
            // A drive already queued/running (e.g. this call is only forcing a re-check of exclusions,
            // not a genuine config change to this specific drive) is left to finish its current pass --
            // it naturally picks up whatever's current the next time it runs, rather than being
            // interrupted and restarted from its last checkpoint for no real reason.
            var needsInitialRefresh = !alreadyActive
                && (cachedDrives == null || !cachedDrives.Contains(drive) || mode == "startup");
            if (needsInitialRefresh)
            {
                QueueRefreshDrive(drive, "configure");
            }
        }
    }

    private void EnsurePeriodicRefreshLocked(string drive, string mode, DateTime? lastUpdated)
    {
        var interval = IndexerHelper.GetRefreshInterval(mode);
        if (interval == null)
        {
            RemovePeriodicLocked(drive);
            return;
        }

        if (_periodicCts.ContainsKey(drive))
            return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _periodicCts[drive] = cts;
        _ = Task.Run(async () =>
        {
            var firstRun = true;
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var delay = interval.Value;
                    if (firstRun && lastUpdated.HasValue)
                    {
                        var timeSinceLastUpdate = DateTime.Now - lastUpdated.Value;
                        if (timeSinceLastUpdate > TimeSpan.Zero)
                        {
                            var remaining = interval.Value - timeSinceLastUpdate;
                            // If overdue or remaining time is less than 5s, delay for 5s to avoid startup bottleneck
                            delay = remaining > TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5);
                        }
                    }
                    firstRun = false;

                    await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                    QueueRefreshDrive(drive, mode);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, CancellationToken.None);
    }

    private void RemovePeriodicLocked(string drive)
    {
        if (_periodicCts.Remove(drive, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    // User-initiated interrupt of a drive that's currently queued or actively refreshing. Removal from
    // _debounceCts/_activeCts still happens in the normal completion paths (QueueRefreshDrive's finally,
    // RefreshDriveLoop's finally), but reverting the status can't wait for those: a drive cancelled during
    // its debounce wait (not yet in _activeCts, so RefreshDrive's own cancellation catch never runs) would
    // otherwise stay stuck on "indexing" forever. Reverting it here too, unconditionally, is safe only
    // because this method is exclusively the user-facing Stop path -- a drive removed from config instead
    // goes through StartRefresh's own cleanup, which never calls this.
    public void CancelDrive(string drive)
    {
        lock (_gate)
        {
            if (_debounceCts.TryGetValue(drive, out var debounce))
                debounce.Cancel();
            if (_activeCts.TryGetValue(drive, out var active))
                active.Cancel();
        }
        _setStatus(drive, "cached", null, null);
    }

    private void RefreshDrive(string drive, CancellationToken token)
    {
        var root = (drive.StartsWith(@"\\") || drive.StartsWith(@"//")) ? drive : drive + @":\";
        if (!root.EndsWith(Path.DirectorySeparatorChar.ToString()) && !root.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
        {
            root += Path.DirectorySeparatorChar;
        }
        var physicalRoot = root;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            _setStatus(drive, "indexing", 0, null);
            var settings = UserSettings.Load();
            var options = new WalkOptions(
                settings.ExcludedPaths,
                settings.IgnoredPathGlobs,
                settings.IgnoredPathRegexes,
                0,
                0,
                true);
            var previousStore = _getPreviousStore(drive);
            LogResumeProgress(drive, previousStore);
            var index = NetworkIndex.Build(
                drive,
                root,
                physicalRoot,
                options,
                token,
                // Fires every ~1024 items from worker threads that only check cancellation at specific
                // points -- unguarded, this can race past CancelDrive's "cached" revert and clobber it
                // back to "indexing", which is exactly what made the Stop button lose that race sometimes.
                count => { if (!token.IsCancellationRequested) _setStatus(drive, "indexing", count, null); },
                (store, stats) => _onPublishCheckpoint(drive, store, stats, token),
                previousStore);
            token.ThrowIfCancellationRequested();
            // Reaching here without cancellation only means TreeBuilder.Run() drained its queue -- NOT
            // that every directory's real contents were captured. A directory that failed to enumerate
            // (network hiccup, permissions) is caught silently in WalkDirectory (CountError + return,
            // never MarkListed), so it correctly stays un-Listed for a future resume to retry -- but that
            // only works if this index isn't marked complete, since IsComplete=true tells Configure() this
            // drive needs no further refresh at all, which would permanently paper over the gap instead.
            index.IsComplete = index.Errors == 0;
            if (!index.IsComplete)
                Logger.Log($"[NetworkIndexer] {drive}: finished with {index.Errors} error(s) ({index.EnumerateErrors} enumerate, {index.AttributeErrors} attribute) -- not marking complete, next refresh will retry the gaps.", LogLevel.Warn);
            IndexerHelper.Save(index);

            stopwatch.Stop();
            Logger.Log($"[NetworkIndexer] {drive}: finished in {stopwatch.Elapsed.TotalSeconds:F1}s, {index.Count} records.");

            _onRefreshFinished(drive, index);
        }
        catch (OperationCanceledException)
        {
            Logger.Log($"[NetworkIndexer] {drive}: refresh cancelled, keeping the last checkpoint.");
            // A drive removed from config already had its status entry deleted (NetworkIndexer.Configure),
            // so SetStatus's own "only update an entry that still exists" guard makes this a no-op there --
            // this only actually reverts the status for a drive a user stopped via CancelDrive while it
            // remains configured, so it shows what's on disk from the last checkpoint instead of being
            // stuck on "indexing" forever.
            _setStatus(drive, "cached", null, null);
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkIndexer] Failed to index {drive}: {ex.Message}", LogLevel.Error);
            _setStatus(drive, "error", null, ex.Message);
        }
    }

    // Directories-listed ratio is a proxy for "how far the previous pass got": a directory only carries
    // FileRecordFlags.Listed once its own children were fully captured, so this is what TreeDiffBaseline
    // will actually be able to trust and skip re-listing, as opposed to just the raw record count.
    private static void LogResumeProgress(string drive, FileRecordStore? previousStore)
    {
        if (previousStore == null)
        {
            Logger.Log($"[NetworkIndexer] {drive}: no previous index to resume from, starting a fresh scan.");
            return;
        }

        var totalDirs = 0;
        var listedDirs = 0;
        foreach (var record in previousStore.Records)
        {
            if (!record.IsDirectory)
                continue;
            totalDirs++;
            if ((record.Flags & FileRecordFlags.Listed) != 0)
                listedDirs++;
        }

        Logger.Log($"[NetworkIndexer] {drive}: resuming with {previousStore.Records.Count} records from last pass " +
            $"({listedDirs}/{totalDirs} directories confirmed listed, previous IsComplete={previousStore.IsComplete}).");
    }

    public void Dispose()
    {
        _lifetimeCts.Cancel();

        lock (_gate)
        {
            foreach (var cts in _periodicCts.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _periodicCts.Clear();

            foreach (var cts in _debounceCts.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _debounceCts.Clear();

            foreach (var cts in _activeCts.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _activeCts.Clear();
        }

        _lifetimeCts.Dispose();
    }
}
