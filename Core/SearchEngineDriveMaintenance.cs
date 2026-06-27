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
            var cached = LocalDriveCacheLocator.ListCachedDrives(IndexCacheDir);
            var visible = detected.Concat(cached).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d).ToList();
            var enabledIds = new HashSet<string>(_settings().LocalDrives, StringComparer.OrdinalIgnoreCase);
            var supported = enabledIds.Count == 0
                ? detected
                : detected.Where(d => enabledIds.Contains(VolumeHelper.GetVolumeId(d) ?? string.Empty)).ToList();
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

        var enabledIds = _settings().LocalDrives;
        var driveId = VolumeHelper.GetVolumeId(drive) ?? string.Empty;
        if (enabledIds.Count > 0 && !enabledIds.Contains(driveId, StringComparer.OrdinalIgnoreCase))
            return false;

        return QueueDriveRebuild(drive, forceRebuild: true);
    }

    public bool DeleteDriveIndex(string drive)
    {
        drive = NormalizeDrive(drive);
        if (drive.Length == 0)
            return false;

        LocalDriveCacheLocator.Delete(IndexCacheDir, drive);
        _indexer.DropDriveFromRuntime(drive);
        var detected = VolumeHelper.DetectIndexableLocalDrives();
        var detectedSet = new HashSet<string>(detected, StringComparer.OrdinalIgnoreCase);
        var enabledIds = new HashSet<string>(_settings().LocalDrives, StringComparer.OrdinalIgnoreCase);
        var isPresent = detectedSet.Contains(drive);
        var isEnabled = enabledIds.Count == 0 ? isPresent : isPresent && enabledIds.Contains(VolumeHelper.GetVolumeId(drive) ?? string.Empty);
        lock (_indexer.LockObj)
        {
            var status = _indexer.Status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
            if (status != null)
            {
                status.Enabled = isEnabled;
                status.Kind = isPresent ? VolumeHelper.GetDisplayFileSystemType(drive) : "-";
                status.State = isPresent ? (isEnabled ? "ready" : "disabled") : "unavailable";
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
            var wasEnabled = existing.Enabled;
            var hasCache = LocalDriveCacheLocator.HasCache(IndexCacheDir, drive);
            existing.Enabled = isPresent && isEnabled;
            existing.Kind = isPresent ? VolumeHelper.GetDisplayFileSystemType(drive) : "-";
            existing.State = isPresent ? existing.State : "unavailable";
            if (!isPresent)
            {
                existing.Files = 0;
                existing.Dirs = 0;
            }
            else if (!wasEnabled && isEnabled && !hasCache && existing.State is not "indexing" and not "pending")
            {
                existing.State = "pending";
                drivesToBuild.Add(drive);
            }
            return existing;
        }

        var shouldBuild = isPresent && isEnabled && !LocalDriveCacheLocator.HasCache(IndexCacheDir, drive);
        if (shouldBuild)
            drivesToBuild.Add(drive);
        return new UsnIndexer.DriveIndexStatus
        {
            Drive = drive,
            Enabled = isPresent && isEnabled,
            Kind = isPresent ? VolumeHelper.GetDisplayFileSystemType(drive) : "-",
            State = shouldBuild ? "pending" : isPresent && isEnabled ? "ready" : isPresent ? "disabled" : "unavailable",
            CachePath = isPresent ? LocalDriveCacheLocator.GetCachePath(IndexCacheDir, drive) : string.Empty
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
        {
            _indexer.SetDriveState(drive, "failed");
            return;
        }

        _indexer.SaveDrivesToCache(IndexCacheDir, metadata);
        EnsureDriveMonitor(drive, metadata[0].JournalId, metadata[0].NextUsn);
    }

    private void EnsureDriveMonitor(string drive, ulong journalId, long nextUsn)
    {
        var fs = VolumeHelper.GetFileSystemType(drive);
        if (fs.Equals("NTFS", StringComparison.OrdinalIgnoreCase) || fs.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
        {
            new UsnMonitor(drive, journalId, nextUsn, _indexer, _token(), QueueDriveRebuild).Start();
            return;
        }

        var monitor = new FolderDriveMonitor(drive, (changeType, path, oldPath) => _indexer.ApplyFolderChange(drive, changeType, path, oldPath), _token());
        monitor.Start();
        _addMonitor(monitor);
    }

    private static string NormalizeDrive(string drive) => string.IsNullOrWhiteSpace(drive)
        ? string.Empty
        : drive.Trim().TrimEnd(':', '\\').ToUpperInvariant();
}
