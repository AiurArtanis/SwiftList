using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core;

internal sealed class SearchEngineDriveMaintenance
{
    private static readonly string IndexCacheDir = Path.Combine(Logger.SharedDataDir, "indexes");
    private readonly UsnIndexer _indexer;
    private readonly Func<MachineSettings> _settings;
    private readonly Func<CancellationToken> _token;
    private readonly Func<bool> _isRebuilding;
    private readonly Action<IDisposable> _addMonitor;
    private readonly HashSet<string> _pendingDriveRebuilds = new(StringComparer.OrdinalIgnoreCase);
    public bool HasPendingRebuilds { get { lock (_pendingDriveRebuilds) return _pendingDriveRebuilds.Count > 0; } }

    public SearchEngineDriveMaintenance(
        UsnIndexer indexer,
        Func<MachineSettings> settings,
        Func<CancellationToken> token,
        Func<bool> isRebuilding,
        Action<IDisposable> addMonitor)
    {
        _indexer = indexer;
        _settings = settings;
        _token = token;
        _isRebuilding = isRebuilding;
        _addMonitor = addMonitor;
    }

    public void RefreshDrivesInStatus()
    {
        try
        {
            var detected = VolumeHelper.DetectIndexableLocalDrives();
            var detectedSet = new HashSet<string>(detected, StringComparer.OrdinalIgnoreCase);
            var cached = FileRecordStoreSerializer.ListSourceKeys(IndexCacheDir)
                .Where(key => key.Length == 1 && char.IsLetter(key[0]));
            var visible = detected.Concat(cached).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d).ToList();
            var enabledSet = new HashSet<string>(MachineSettings.Load().EnabledLocalDrives, StringComparer.OrdinalIgnoreCase);
            var supported = enabledSet.Count == 0 ? detected : detected.Where(enabledSet.Contains).ToList();
            var enabled = new HashSet<string>(supported, StringComparer.OrdinalIgnoreCase);
            var drivesToBuild = new List<string>();

            lock (_indexer.LockObj)
            {
                var current = _indexer.Status.Drives.ToDictionary(d => d.Drive, StringComparer.OrdinalIgnoreCase);
                var next = new List<UsnIndexer.DriveIndexStatus>();
                foreach (var drive in visible)
                    next.Add(UpdateStatus(drive, detectedSet.Contains(drive), enabled.Contains(drive), current, drivesToBuild));
                _indexer.Status.Drives = next;
            }

            foreach (var drive in drivesToBuild)
                QueueDriveRebuild(drive);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchEngine] Failed to refresh drive statuses: {ex.Message}", LogLevel.Error);
        }
    }

    public bool RebuildDriveIndex(string drive)
    {
        drive = NormalizeDrive(drive);
        if (drive.Length == 0)
            return false;

        var enabledDrives = _settings().EnabledLocalDrives;
        if (enabledDrives.Count > 0 && !enabledDrives.Contains(drive, StringComparer.OrdinalIgnoreCase))
            return false;

        return QueueDriveRebuild(drive, forceRebuild: true);
    }

    public bool DeleteDriveIndex(string drive)
    {
        drive = NormalizeDrive(drive);
        if (drive.Length == 0)
            return false;

        FileRecordStoreSerializer.Delete(IndexCacheDir, drive);
        _indexer.DropDriveFromRuntime(drive);
        lock (_indexer.LockObj)
        {
            var status = _indexer.Status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
            if (status != null)
            {
                status.State = "disabled";
                status.Files = 0;
                status.Dirs = 0;
            }
        }
        Logger.Log($"[SearchEngine] Deleted cached index for drive {drive} by client request.");
        return true;
    }

    public void QueueDriveRebuild(string drive) => QueueDriveRebuild(drive, forceRebuild: false);

    private bool QueueDriveRebuild(string drive, bool forceRebuild)
    {
        lock (_pendingDriveRebuilds)
        {
            if (_isRebuilding())
            {
                Logger.Log($"[SearchEngine] Ignored drive {drive} rebuild request because a full rebuild is running.");
                return false;
            }

            if (forceRebuild && _pendingDriveRebuilds.Count > 0)
            {
                Logger.Log($"[SearchEngine] Ignored drive {drive} rebuild request because another drive rebuild is running.");
                return false;
            }

            if (!_pendingDriveRebuilds.Add(drive))
            {
                Logger.Log($"[SearchEngine] Ignored duplicate rebuild request for drive {drive}.");
                return false;
            }
        }
        _indexer.SetDriveState(drive, "indexing", resetCounts: true);
        Task.Run(() => RebuildDrive(drive, forceRebuild));
        return true;
    }

    private UsnIndexer.DriveIndexStatus UpdateStatus(
        string drive,
        bool isPresent,
        bool isEnabled,
        Dictionary<string, UsnIndexer.DriveIndexStatus> current,
        List<string> drivesToBuild)
    {
        if (current.TryGetValue(drive, out var existing))
        {
            existing.Enabled = isPresent && isEnabled;
            existing.Kind = isPresent ? VolumeHelper.GetDisplayFileSystemType(drive) : "-";
            existing.State = isPresent ? existing.State : "unavailable";
            if (!isPresent)
            {
                existing.Files = 0;
                existing.Dirs = 0;
            }
            return existing;
        }

        if (isPresent && isEnabled)
            drivesToBuild.Add(drive);
        return new UsnIndexer.DriveIndexStatus
        {
            Drive = drive,
            Enabled = isPresent && isEnabled,
            Kind = isPresent ? VolumeHelper.GetDisplayFileSystemType(drive) : "-",
            State = isPresent && isEnabled ? "pending" : isPresent ? "disabled" : "unavailable",
            CachePath = FileRecordStoreSerializer.GetBasePath(IndexCacheDir, drive) + ".meta"
        };
    }

    private void RebuildDrive(string drive, bool forceRebuild)
    {
        try
        {
            if (forceRebuild)
                ForceRebuildDrive(drive);
            else
                DriveRecovery.RestoreOrRebuild(_indexer, IndexCacheDir, drive, _token(), QueueDriveRebuild, _addMonitor);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchEngine] Failed to build drive {drive}: {ex.Message}", LogLevel.Error);
            _indexer.SetDriveState(drive, "failed");
        }
        finally
        {
            lock (_pendingDriveRebuilds)
                _pendingDriveRebuilds.Remove(drive);
        }
    }

    private void ForceRebuildDrive(string drive)
    {
        Logger.Log($"[SearchEngine] Rebuilding drive {drive} by client request.");
        _indexer.SetDriveState(drive, "indexing");
        var metadata = _indexer.BuildDrives(new[] { drive }, clearExisting: false);
        if (metadata.Count == 0)
            _indexer.SetDriveState(drive, "failed");
        else
            _indexer.SaveDrivesToCache(IndexCacheDir, metadata);
    }

    private static string NormalizeDrive(string drive) => string.IsNullOrWhiteSpace(drive)
        ? string.Empty
        : drive.Trim().TrimEnd(':', '\\').ToUpperInvariant();
}
