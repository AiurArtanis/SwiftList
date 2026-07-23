using SwiftList.Core.Indexer.Usn;

using SwiftList.Core.DriveMonitoring;
namespace SwiftList.Core;

public class SearchEngine : IDisposable
{
    private readonly UsnIndexer _indexer = new();
    private CancellationTokenSource? _cts;
    private readonly object _startLock = new();
    private bool _isRebuilding = false;
    private MachineSettings _machineSettings = MachineSettings.Load();
    private readonly SearchEngineDriveMaintenance _drives;

    // Search cancellation
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _searchDirCts;
    private readonly object _searchLock = new();
    private static readonly string IndexCacheDir = Path.Combine(Logger.SharedDataDir, "indexes");

    private long _lastSearchTimeTicks = Environment.TickCount64;
    private bool _needsTrim;
    private long _lastDriveDetectTime = 0;
    private readonly object _trimLock = new();
    private readonly Timer? _idleTimer;
    private readonly List<IDisposable> _folderMonitors = new();

    public SearchEngine()
    {
        _drives = new SearchEngineDriveMaintenance(
            _indexer,
            () => _machineSettings,
            () => _cts?.Token ?? CancellationToken.None,
            () => _isRebuilding,
            AddFolderMonitor,
            TryReleaseRuntimeAfterActivity);
        _idleTimer = new Timer(OnIdleTimerTick, null, 3000, 3000);
    }

    public event Action<UsnIndexer.IndexerStatus> StatusChanged
    {
        add => _indexer.StatusChanged += value;
        remove => _indexer.StatusChanged -= value;
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
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastSearchTimeTicks);
        if (now - last > 3000) // 3 seconds idle
        {
            var shouldTrim = false;
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
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                Win32Api.TrimWorkingSet();
            }
        }
    }

    public Dictionary<string, FileMetadataEntry> GetFileMetadataBatch(IReadOnlyList<string> paths) => _indexer.GetFileMetadataBatch(paths);

    public void ClearPathCaches() => _indexer.ClearAllPathCaches();

    public List<SearchResult> GetRecentFiles(IReadOnlyList<string> directories, int limit, int maxAgeMinutes) => _indexer.GetRecentFiles(directories, limit, maxAgeMinutes);

    public UsnIndexer.IndexerStatus GetStatus()
    {
        _indexer.Status.IsMaintenanceBusy = _isRebuilding || _drives.HasPendingRebuilds;
        var now = Environment.TickCount64;
        if (now - _lastDriveDetectTime > 5000 && (_indexer.Status.State is "ready" or "idle"))
        {
            _lastDriveDetectTime = now;
            RefreshDrivesInStatus();
        }
        return _drives.BuildStatusSnapshot();
    }

    private void RefreshDrivesInStatus()
        => _drives.RefreshDrivesInStatus();

    public bool RebuildDriveIndex(string drive) => _drives.RebuildDriveIndex(drive);

    public bool DeleteDriveIndex(string drive) => _drives.DeleteDriveIndex(drive);

    public MachineSettings GetMachineSettings() => _machineSettings;

    public void UpdateMachineSettings(MachineSettings settings)
    {
        var oldDrives = _machineSettings?.LocalDrives ?? new List<string>();
        var newDrives = settings.LocalDrives ?? new List<string>();

        var drivesChanged = !oldDrives.OrderBy(d => d).SequenceEqual(newDrives.OrderBy(d => d), StringComparer.OrdinalIgnoreCase);

        _machineSettings = settings;
        _machineSettings.Save();

        if (drivesChanged)
        {
            RefreshDrivesInStatus();
        }
    }


    public bool SearchStreaming(
        string query,
        int fileLimit,
        int appLimit,
        string? directoryFilter,
        Action<SearchResult> onResult,
        CancellationToken requestToken = default)
    {
        RecordSearchActivity();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        CancellationTokenSource searchCts;
        lock (_searchLock)
        {
            if (string.IsNullOrEmpty(directoryFilter))
            {
                _searchCts?.Cancel();
                _searchCts = new CancellationTokenSource();
                searchCts = _searchCts;
            }
            else
            {
                _searchDirCts?.Cancel();
                _searchDirCts = new CancellationTokenSource();
                searchCts = _searchDirCts;
            }
        }

        var status = GetStatus();
        if (status.State != "ready")
        {
            Logger.Log($"[SearchEngine] File search skipped because index is not ready. State: {status.State}", LogLevel.Warn);
            return true;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(searchCts.Token, requestToken);
        var searchToken = linkedCts.Token;

        _indexer.SearchStreaming(query, fileLimit, result =>
        {
            searchToken.ThrowIfCancellationRequested();
            onResult(result);
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
        lock (_indexer.LockObj)
        {
            _indexer.Status.State = forceRebuild ? "indexing" : "pending";
            _indexer.Status.Progress = 0;
        }
        _indexer.NotifyStatusChanged();

        Task.Run(() =>
        {
            // Cancel any active monitors
            _cts?.Cancel();
            _cts?.Dispose();
            DisposeFolderMonitors();
            _cts = new CancellationTokenSource();

            var initializer = new SearchEngineInitializer(_indexer, IndexCacheDir, _drives.QueueDriveRebuild, AddFolderMonitor);
            initializer.Run(forceRebuild, _cts, isRebuilding =>
            {
                lock (_startLock)
                {
                    _isRebuilding = isRebuilding;
                }
                if (!isRebuilding)
                    TryReleaseRuntimeAfterActivity();
            });
        });
    }

    public void Dispose()
    {
        _idleTimer?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        DisposeFolderMonitors();
        lock (_searchLock)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchDirCts?.Cancel();
            _searchDirCts?.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private void AddFolderMonitor(IDisposable monitor)
    {
        lock (_folderMonitors)
            _folderMonitors.Add(monitor);
    }

    private void DisposeFolderMonitors()
    {
        lock (_folderMonitors)
        {
            foreach (var monitor in _folderMonitors)
                monitor.Dispose();
            _folderMonitors.Clear();
        }
    }

    private void TryReleaseRuntimeAfterActivity()
    {
        if (_isRebuilding)
            return;

        _indexer.ClearCaches();
        Task.Run(async () =>
        {
            await Task.Delay(150);
            _indexer.CompactMemory();
        });
    }
}
