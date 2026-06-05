using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.Indexer.NetworkDrive
{
    public sealed class NetworkIndexer : IDisposable
    {
        private readonly object _gate = new();
        internal object Gate => _gate;
        internal readonly Dictionary<string, NetworkIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NetworkIndexStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _refreshModes = new(StringComparer.OrdinalIgnoreCase);
        private bool _configured;

        private WatcherManager? _watcherManager;
        private Scheduler? _scheduler;

        public NetworkIndexer()
        {
            _watcherManager = new WatcherManager(
                (drive, reason) => _scheduler?.QueueRefreshDrive(drive, reason),
                drive => { lock (_gate) { _indexes.TryGetValue(drive, out var idx); return idx; } },
                (drive, idx) => PublishIncrementalUpdate(drive, idx)
            );

            _scheduler = new Scheduler(
                (drive, mode) => _watcherManager?.EnsureWatcher(drive),
                drive => _watcherManager?.RemoveWatcher(drive),
                SetStatus,
                OnRefreshFinished,
                PublishCheckpoint
            );
        }

        public void EnsureConfigured()
        {
            if (_configured)
                return;

            lock (_gate)
            {
                if (_configured)
                    return;

                var settings = UserSettings.Load();
                Configure(settings.NetworkDrives);
                _configured = true;
            }
        }

        public void Configure(IEnumerable<NetworkDriveSetting> driveSettings, bool forceRefresh = false)
        {
            var enabledSettings = driveSettings
                .Where(d => d.Enabled)
                .Select(d => new
                {
                    Drive = IndexerHelper.NormalizeDrive(d.Drive),
                    RefreshMode = IndexerHelper.NormalizeRefreshMode(d.RefreshMode)
                })
                .Where(d => d.Drive.Length == 1)
                .GroupBy(d => d.Drive, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            var enabledDrives = enabledSettings.Select(d => d.Drive).ToList();
            var refreshModes = enabledSettings.ToDictionary(d => d.Drive, d => d.RefreshMode, StringComparer.OrdinalIgnoreCase);

            var localFolderDrives = VolumeHelper.DetectFolderIndexDrives();
            foreach (var drive in localFolderDrives)
            {
                if (!enabledDrives.Contains(drive, StringComparer.OrdinalIgnoreCase))
                {
                    enabledDrives.Add(drive);
                    refreshModes[drive] = "startup";
                }
            }

            var cachedDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lastUpdatedTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            lock (_gate)
            {
                foreach (string removed in _indexes.Keys.Except(enabledDrives, StringComparer.OrdinalIgnoreCase).ToList())
                {
                    _indexes.Remove(removed);
                    _statuses.Remove(removed);
                    _refreshModes.Remove(removed);
                }

                foreach (string drive in enabledDrives)
                {
                    _refreshModes[drive] = refreshModes[drive];
                    if (!_statuses.ContainsKey(drive))
                    {
                        _statuses[drive] = new NetworkIndexStatus
                        {
                            Drive = drive,
                            State = "pending",
                            CachePath = IndexerHelper.GetCachePath(drive)
                        };
                    }

                    if (!_indexes.ContainsKey(drive))
                    {
                        if (IndexerHelper.TryLoad(drive, out var index))
                        {
                            _indexes[drive] = index;
                            _statuses[drive] = new NetworkIndexStatus
                            {
                                Drive = drive,
                                State = "cached",
                                Items = index.Count,
                                CachePath = IndexerHelper.GetCachePath(drive),
                                LastUpdated = index.LastUpdated
                            };
                            cachedDrives.Add(drive);
                            lastUpdatedTimes[drive] = index.LastUpdated;
                        }
                    }
                    else
                    {
                        cachedDrives.Add(drive);
                        lastUpdatedTimes[drive] = _indexes[drive].LastUpdated;
                    }
                }
            }

            _scheduler?.StartRefresh(enabledDrives, refreshModes, forceRefresh ? null : cachedDrives, forceRefresh ? null : lastUpdatedTimes);
        }

        public IReadOnlyList<NetworkIndexStatus> GetStatuses()
        {
            EnsureConfigured();
            lock (_gate)
                return _statuses.Values.Select(s => s.Clone()).OrderBy(s => s.Drive).ToList();
        }



        private void SetStatus(string drive, string state, int? items, string? error)
        {
            lock (_gate)
            {
                _statuses.TryGetValue(drive, out var current);
                _statuses[drive] = new NetworkIndexStatus
                {
                    Drive = drive,
                    State = state,
                    Items = items ?? current?.Items ?? 0,
                    Skipped = current?.Skipped ?? 0,
                    Errors = current?.Errors ?? 0,
                    EnumerateErrors = current?.EnumerateErrors ?? 0,
                    AttributeErrors = current?.AttributeErrors ?? 0,
                    ReparseSkipped = current?.ReparseSkipped ?? 0,
                    SlowDirectories = current?.SlowDirectories ?? 0,
                    CachePath = IndexerHelper.GetCachePath(drive),
                    LastUpdated = current?.LastUpdated,
                    Error = error ?? string.Empty
                };
            }
        }

        private void OnRefreshFinished(string drive, NetworkIndex index)
        {
            lock (_gate)
            {
                _indexes[drive] = index;
                _statuses[drive] = new NetworkIndexStatus
                {
                    Drive = drive,
                    State = "ready",
                    Items = index.Count,
                    Skipped = index.Skipped,
                    Errors = index.Errors,
                    EnumerateErrors = index.EnumerateErrors,
                    AttributeErrors = index.AttributeErrors,
                    ReparseSkipped = index.ReparseSkipped,
                    SlowDirectories = index.SlowDirectories,
                    CachePath = IndexerHelper.GetCachePath(drive),
                    LastUpdated = index.LastUpdated
                };
            }
        }

        private void PublishIncrementalUpdate(string drive, NetworkIndex index)
        {
            IndexerHelper.Save(index);
            lock (_gate)
            {
                _statuses.TryGetValue(drive, out var current);
                _statuses[drive] = new NetworkIndexStatus
                {
                    Drive = drive,
                    State = "ready",
                    Items = index.Count,
                    Skipped = index.Skipped,
                    Errors = index.Errors,
                    EnumerateErrors = index.EnumerateErrors,
                    AttributeErrors = index.AttributeErrors,
                    ReparseSkipped = index.ReparseSkipped,
                    SlowDirectories = index.SlowDirectories,
                    CachePath = current?.CachePath ?? IndexerHelper.GetCachePath(drive),
                    LastUpdated = index.LastUpdated,
                    Error = string.Empty,
                };
            }
        }

        private void PublishCheckpoint(string drive, FileRecordStore store, NetworkDriveWalkStats stats, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                var index = NetworkIndex.FromStore(store, stats);
                IndexerHelper.Save(index);

                lock (_gate)
                {
                    _indexes[drive] = index;
                    _statuses[drive] = new NetworkIndexStatus
                    {
                        Drive = drive,
                        State = "indexing",
                        Items = index.Count,
                        Skipped = index.Skipped,
                        Errors = index.Errors,
                        EnumerateErrors = index.EnumerateErrors,
                        AttributeErrors = index.AttributeErrors,
                        ReparseSkipped = index.ReparseSkipped,
                        SlowDirectories = index.SlowDirectories,
                        CachePath = IndexerHelper.GetCachePath(drive),
                        LastUpdated = index.LastUpdated
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log($"[NetworkIndexer] Failed to publish checkpoint for {drive}: {ex.Message}", SwiftList.Core.LogLevel.Error);
            }
        }



        public void Dispose()
        {
            _scheduler?.Dispose();
            _scheduler = null;

            _watcherManager?.Dispose();
            _watcherManager = null;
        }
    }
}
