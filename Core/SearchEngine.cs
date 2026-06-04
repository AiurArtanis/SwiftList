using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core
{
    public class SearchEngine : IDisposable
    {
        private readonly UsnIndexer _indexer = new();
        private readonly StartMenuAppIndex _appIndex = new();
        private CancellationTokenSource? _cts;
        private readonly object _startLock = new();
        private bool _isRebuilding = false;
        private MachineSettings _machineSettings = MachineSettings.Load();

        // Search cancellation
        private CancellationTokenSource? _searchCts;
        private readonly object _searchLock = new();
        private static readonly string IndexCacheDir = Path.Combine(Logger.SharedDataDir, "indexes");

        private long _lastSearchTimeTicks = Environment.TickCount64;
        private bool _needsTrim;
        private long _lastDriveDetectTime = 0;
        private readonly object _trimLock = new();
        private readonly Timer? _idleTimer;

        public SearchEngine()
        {
            _appIndex.Refresh();
            _idleTimer = new Timer(OnIdleTimerTick, null, 3000, 3000);
        }

        private void RecordSearchActivity()
        {
            Interlocked.Exchange(ref _lastSearchTimeTicks, Environment.TickCount64);
            lock (_trimLock)
            {
                _needsTrim = true;
            }
        }

        private void OnIdleTimerTick(object? state)
        {
            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref _lastSearchTimeTicks);
            if (now - last > 3000) // 3 seconds idle
            {
                bool shouldTrim = false;
                lock (_trimLock)
                {
                    if (_needsTrim)
                    {
                        _needsTrim = false;
                        shouldTrim = true;
                    }
                }

                if (shouldTrim)
                {
                    Logger.Log("[SearchEngine] Service has been idle for 3s. Trimming working set...", LogLevel.Debug);
                    _indexer.ClearCaches();
                    Win32Api.TrimWorkingSet();
                }
            }
        }

        public UsnIndexer.IndexerStatus GetStatus()
        {
            long now = Environment.TickCount64;
            if (now - _lastDriveDetectTime > 5000 && (_indexer.Status.State is "ready" or "idle"))
            {
                _lastDriveDetectTime = now;
                RefreshDrivesInStatus();
            }
            return _indexer.Status;
        }

        private void RefreshDrivesInStatus()
        {
            try
            {
                var detectedDrives = VolumeHelper.DetectSupportedDrives();
                var machineSettings = MachineSettings.Load();
                var enabledSet = new HashSet<string>(machineSettings.EnabledLocalDrives, StringComparer.OrdinalIgnoreCase);
                var supportedDrives = enabledSet.Count == 0
                    ? detectedDrives
                    : detectedDrives.Where(enabledSet.Contains).ToList();

                var enabled = new HashSet<string>(supportedDrives, StringComparer.OrdinalIgnoreCase);

                lock (_indexer.LockObj)
                {
                    var currentDrives = _indexer.Status.Drives.ToDictionary(d => d.Drive, StringComparer.OrdinalIgnoreCase);
                    var newDrivesList = new List<UsnIndexer.DriveIndexStatus>();

                    foreach (var d in detectedDrives)
                    {
                        if (currentDrives.TryGetValue(d, out var existing))
                        {
                            existing.Enabled = enabled.Contains(d);
                            newDrivesList.Add(existing);
                        }
                        else
                        {
                            newDrivesList.Add(new UsnIndexer.DriveIndexStatus
                            {
                                Drive = d,
                                Enabled = enabled.Contains(d),
                                Kind = VolumeHelper.GetFileSystemType(d),
                                State = enabled.Contains(d) ? "pending" : "disabled",
                                CachePath = Path.Combine(IndexCacheDir, d + ".meta")
                            });
                        }
                    }

                    _indexer.Status.Drives = newDrivesList;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchEngine] Failed to refresh drive statuses: {ex.Message}", LogLevel.Error);
            }
        }

        public MachineSettings GetMachineSettings() => _machineSettings;

        public void UpdateMachineSettings(MachineSettings settings)
        {
            var oldDrives = _machineSettings?.EnabledLocalDrives ?? new List<string>();
            var newDrives = settings.EnabledLocalDrives ?? new List<string>();

            bool drivesChanged = !oldDrives.OrderBy(d => d).SequenceEqual(newDrives.OrderBy(d => d), StringComparer.OrdinalIgnoreCase);

            _machineSettings = settings;
            _machineSettings.Save();

            if (drivesChanged)
            {
                InitializeOrLoadIndex(forceRebuild: false);
            }
        }

        public SearchResponse Search(string query, int fileLimit = 1000, int appLimit = 1000, string? directoryFilter = null, CancellationToken requestToken = default)
        {
            RecordSearchActivity();
            if (string.IsNullOrWhiteSpace(query))
            {
                return new SearchResponse();
            }

            // Cancel any previous search
            CancellationTokenSource searchCts;
            lock (_searchLock)
            {
                _searchCts?.Cancel();
                _searchCts = new CancellationTokenSource();
                searchCts = _searchCts;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(searchCts.Token, requestToken);
            CancellationToken searchToken = linkedCts.Token;

            var parsed = SearchQueryParser.Parse(query);
            var appResults = parsed.IsPathMode
                ? new List<SearchResult>()
                : _appIndex.Search(query, appLimit, searchToken);

            var status = GetStatus();
            if (status.State != "ready")
            {
                Logger.Log($"[SearchEngine] File search skipped because index is not ready. State: {status.State}", SwiftList.Core.LogLevel.Warn);
                return new SearchResponse
                {
                    AppResults = appResults
                };
            }

            return new SearchResponse
            {
                FileResults = _indexer.Search(query, fileLimit, searchToken, directoryFilter),
                AppResults = appResults
            };
        }

        public bool SearchStreaming(
            string query,
            int fileLimit,
            int appLimit,
            string? directoryFilter,
            Action<SearchResult, bool> onResult,
            CancellationToken requestToken = default)
        {
            RecordSearchActivity();
            if (string.IsNullOrWhiteSpace(query))
                return true;

            CancellationTokenSource searchCts;
            lock (_searchLock)
            {
                _searchCts?.Cancel();
                _searchCts = new CancellationTokenSource();
                searchCts = _searchCts;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(searchCts.Token, requestToken);
            CancellationToken searchToken = linkedCts.Token;

            var parsed = SearchQueryParser.Parse(query);
            if (!parsed.IsPathMode)
            {
                foreach (var result in _appIndex.Search(query, appLimit, searchToken))
                {
                    searchToken.ThrowIfCancellationRequested();
                    onResult(result, true);
                }
            }

            var status = GetStatus();
            if (status.State != "ready")
            {
                Logger.Log($"[SearchEngine] File search skipped because index is not ready. State: {status.State}", SwiftList.Core.LogLevel.Warn);
                return true;
            }

            _indexer.SearchStreaming(query, fileLimit, result =>
            {
                searchToken.ThrowIfCancellationRequested();
                onResult(result, false);
            }, searchToken, directoryFilter);

            return true;
        }

        public void InitializeOrLoadIndex(bool forceRebuild = false)
        {
            lock (_startLock)
            {
                if (_isRebuilding) return;
                _isRebuilding = true;
            }

            Task.Run(() =>
            {
                _appIndex.Refresh();

                // Cancel any active monitors
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                var initializer = new SearchEngineInitializer(_indexer, _appIndex, IndexCacheDir);
                initializer.Run(forceRebuild, _cts, isRebuilding =>
                {
                    lock (_startLock)
                    {
                        _isRebuilding = isRebuilding;
                    }
                });
            });
        }

        public void Dispose()
        {
            _idleTimer?.Dispose();
            _cts?.Cancel();
            _cts?.Dispose();
            lock (_searchLock)
            {
                _searchCts?.Cancel();
                _searchCts?.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}
