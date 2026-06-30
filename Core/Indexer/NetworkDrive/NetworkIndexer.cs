namespace SwiftList.Core.Indexer.NetworkDrive;

public sealed class NetworkIndexer : IDisposable
{
    public event Action<IReadOnlyList<NetworkIndexStatus>>? StatusesChanged;

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
            _configured = true;
            try
            {
                Configure(settings.NetworkDrives);
            }
            catch
            {
                _configured = false;
                throw;
            }
        }
    }

    public void Configure(IEnumerable<NetworkDriveSetting> driveSettings, bool forceRefresh = false)
    {
        var enabledSettings = driveSettings
            .Select(d => new
            {
                Drive = NetworkIndexerHelper.ResolveDriveFromId(d.Id),
                RefreshMode = IndexerHelper.NormalizeRefreshMode(d.RefreshMode)
            })
            .Where(d => d.Drive.Length == 1)
            .GroupBy(d => d.Drive, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        var enabledDrives = enabledSettings.Select(d => d.Drive).ToList();
        var refreshModes = enabledSettings.ToDictionary(d => d.Drive, d => d.RefreshMode, StringComparer.OrdinalIgnoreCase);

        var cachedDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastUpdatedTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            foreach (var removed in _indexes.Keys.Except(enabledDrives, StringComparer.OrdinalIgnoreCase).ToList())
            {
                _indexes.Remove(removed);
                _statuses.Remove(removed);
                _refreshModes.Remove(removed);
            }

            foreach (var drive in enabledDrives)
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
                        _statuses[drive] = NetworkIndexerHelper.CreateStatus(drive, "cached", index.Count, index, null);
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
        PublishStatusesChanged();
    }

    public bool RefreshDrive(string drive)
    {
        EnsureConfigured();
        drive = IndexerHelper.NormalizeDrive(drive);
        if (drive.Length == 0)
            return false;

        lock (_gate)
        {
            if (!_refreshModes.ContainsKey(drive))
                return false;
            if (_statuses.Values.Any(s => s.State is "indexing" or "pending"))
                return false;
        }

        SetStatus(drive, "indexing", 0, null);
        _scheduler?.QueueRefreshDrive(drive, "manual");
        return true;
    }

    public IReadOnlyList<NetworkIndexStatus> GetStatuses()
    {
        EnsureConfigured();
        lock (_gate)
            return _statuses.Values.Select(s => s.Clone()).OrderBy(s => s.Drive).ToList();
    }

    public void DeleteCache(string drive)
    {
        drive = IndexerHelper.NormalizeDrive(drive);
        if (drive.Length == 0)
            return;

        IndexerHelper.DeleteCache(drive);
        lock (_gate)
        {
            _indexes.Remove(drive);
            _statuses.Remove(drive);
        }
        PublishStatusesChanged();
    }

    private void SetStatus(string drive, string state, int? items, string? error)
    {
        lock (_gate)
        {
            _statuses.TryGetValue(drive, out var current);
            _statuses[drive] = NetworkIndexerHelper.CreateStatus(
                drive, state, items ?? current?.Items ?? 0, null, current, error ?? string.Empty);
        }
        PublishStatusesChanged();
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
            IndexerHelper.Save(index);

            lock (_gate)
            {
                _indexes[drive] = index;
                _statuses[drive] = NetworkIndexerHelper.CreateStatus(drive, "indexing", index.Count, index, null);
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

    public void Dispose()
    {
        _scheduler?.Dispose();
        _scheduler = null;

        _watcherManager?.Dispose();
        _watcherManager = null;
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
