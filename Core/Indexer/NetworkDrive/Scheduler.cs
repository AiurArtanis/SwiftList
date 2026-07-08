using System.Diagnostics;

namespace SwiftList.Core.Indexer.NetworkDrive;

internal sealed partial class Scheduler : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _debounceCts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _periodicCts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _refreshingDrives = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingRefreshDrives = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _refreshCts;

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
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();

        lock (_gate)
        {
            var removedDrives = _debounceCts.Keys.Except(drives, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var removed in removedDrives)
            {
                _onWatcherRemove(removed);
                RemovePeriodicLocked(removed);
                if (_debounceCts.Remove(removed, out var debounce))
                {
                    debounce.Cancel();
                    debounce.Dispose();
                }
                _pendingRefreshDrives.Remove(removed);
                _refreshingDrives.Remove(removed);
            }

            foreach (var removed in _periodicCts.Keys.Except(drives, StringComparer.OrdinalIgnoreCase).ToList())
                RemovePeriodicLocked(removed);

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
            var needsInitialRefresh = cachedDrives == null || !cachedDrives.Contains(drive) || mode == "startup";
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

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_refreshCts!.Token);
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
                count => _setStatus(drive, "indexing", count, null),
                (store, stats) => _onPublishCheckpoint(drive, store, stats, token),
                previousStore);
            token.ThrowIfCancellationRequested();
            // Only reached once TreeBuilder.Run() drained every worker without cancellation -- the walk
            // genuinely covered the whole tree, so a future resume can trust every FileRecordFlags.Listed
            // directory this build produced.
            index.IsComplete = true;
            IndexerHelper.Save(index);

            stopwatch.Stop();
            Logger.Log($"[NetworkIndexer] {drive}: finished in {stopwatch.Elapsed.TotalSeconds:F1}s, {index.Count} records.");

            _onRefreshFinished(drive, index);
        }
        catch (OperationCanceledException)
        {
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
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();

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
        }
    }
}
