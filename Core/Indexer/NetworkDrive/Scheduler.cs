using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftList.Core.Indexer.NetworkDrive
{
    internal sealed class Scheduler : IDisposable
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

        public Scheduler(
            Action<string, string> onWatcherEnsure,
            Action<string> onWatcherRemove,
            Action<string, string, int?, string?> setStatus,
            Action<string, NetworkIndex> onRefreshFinished,
            Action<string, FileRecordStore, NetworkDriveWalkStats, CancellationToken> onPublishCheckpoint)
        {
            _onWatcherEnsure = onWatcherEnsure;
            _onWatcherRemove = onWatcherRemove;
            _setStatus = setStatus;
            _onRefreshFinished = onRefreshFinished;
            _onPublishCheckpoint = onPublishCheckpoint;
        }

        public void StartRefresh(IReadOnlyList<string> drives, IReadOnlyDictionary<string, string> refreshModes)
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = new CancellationTokenSource();

            lock (_gate)
            {
                var removedDrives = _debounceCts.Keys.Except(drives, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (string removed in removedDrives)
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

                foreach (string removed in _periodicCts.Keys.Except(drives, StringComparer.OrdinalIgnoreCase).ToList())
                    RemovePeriodicLocked(removed);

                foreach (string drive in drives)
                {
                    string mode = refreshModes.TryGetValue(drive, out var value) ? value : "Manual";
                    _onWatcherEnsure(drive, mode);
                    EnsurePeriodicRefreshLocked(drive, mode);
                }
            }

            foreach (string drive in drives)
            {
                QueueRefreshDrive(drive, "configure");
            }
        }

        private void EnsurePeriodicRefreshLocked(string drive, string mode)
        {
            TimeSpan? interval = IndexerHelper.GetRefreshInterval(mode);
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
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(interval.Value, cts.Token).ConfigureAwait(false);
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

        public void QueueRefreshDrive(string drive, string reason)
        {
            CancellationTokenSource? oldDebounce = null;
            CancellationTokenSource debounce;
            lock (_gate)
            {
                if (_refreshCts == null || _refreshCts.IsCancellationRequested)
                    return;

                if (_debounceCts.TryGetValue(drive, out oldDebounce))
                    oldDebounce.Cancel();

                debounce = CancellationTokenSource.CreateLinkedTokenSource(_refreshCts.Token);
                _debounceCts[drive] = debounce;
            }

            oldDebounce?.Dispose();
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(reason == "configure" ? TimeSpan.Zero : TimeSpan.FromSeconds(2), debounce.Token).ConfigureAwait(false);
                    StartRefreshDriveIfIdle(drive, reason, debounce.Token);
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    lock (_gate)
                    {
                        if (_debounceCts.TryGetValue(drive, out var current) && ReferenceEquals(current, debounce))
                            _debounceCts.Remove(drive);
                    }

                    debounce.Dispose();
                }
            }, CancellationToken.None);
        }

        private void StartRefreshDriveIfIdle(string drive, string reason, CancellationToken token)
        {
            lock (_gate)
            {
                if (token.IsCancellationRequested || _refreshCts == null || _refreshCts.IsCancellationRequested)
                    return;

                if (_refreshingDrives.Contains(drive))
                {
                    _pendingRefreshDrives.Add(drive);
                    return;
                }

                _refreshingDrives.Add(drive);
            }

            _ = Task.Run(() => RefreshDriveLoop(drive, reason, token), token);
        }

        private void RefreshDriveLoop(string drive, string reason, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    Logger.Log($"[NetworkIndexer] Refreshing {drive}: because {reason}");
                    RefreshDrive(drive, token);

                    lock (_gate)
                    {
                        if (!_pendingRefreshDrives.Remove(drive))
                            break;
                    }

                    reason = "pending changes";
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_gate)
                {
                    _refreshingDrives.Remove(drive);
                }
            }
        }

        private void RefreshDrive(string drive, CancellationToken token)
        {
            string root = drive + @":\";
            string physicalRoot = root;

            if (!System.IO.Directory.Exists(root))
            {
                string? uncPath = NetworkDriveResolver.ResolveToUnc(drive);
                if (!string.IsNullOrEmpty(uncPath))
                {
                    physicalRoot = uncPath;
                    Logger.Log($"[NetworkIndexer] Mapped drive {drive}: is not directly accessible. Resolved to UNC path: {uncPath}");
                }
            }

            try
            {
                _setStatus(drive, "indexing", 0, null);
                var settings = UserSettings.Load();
                var options = new WalkOptions(
                    settings.ExcludedPaths,
                    settings.IgnoredPathGlobs,
                    settings.IgnoredPathRegexes,
                    false,
                    false,
                    0,
                    0,
                    true);
                var index = NetworkIndex.Build(
                    drive,
                    root,
                    physicalRoot,
                    options,
                    token,
                    count => _setStatus(drive, "indexing", count, null),
                    (store, stats) => _onPublishCheckpoint(drive, store, stats, token));
                token.ThrowIfCancellationRequested();
                IndexerHelper.Save(index);

                _onRefreshFinished(drive, index);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Log($"[NetworkIndexer] Failed to index {drive}: {ex.Message}");
                _setStatus(drive, "error", null, ex.Message);
            }
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
}
