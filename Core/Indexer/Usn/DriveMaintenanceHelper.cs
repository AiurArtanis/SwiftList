namespace SwiftList.Core.Indexer.Usn;

internal static class DriveMaintenanceHelper
{
    public static string NormalizeDrive(string drive) => string.IsNullOrWhiteSpace(drive)
        ? string.Empty
        : drive.Trim().TrimEnd(':', '\\').ToUpperInvariant();

    public static UsnIndexer.DriveIndexStatus UpdateStatus(
        string drive,
        bool isPresent,
        bool isEnabled,
        string indexCacheDir,
        Dictionary<string, UsnIndexer.DriveIndexStatus> current,
        List<string> drivesToBuild)
    {
        if (current.TryGetValue(drive, out var existing))
        {
            var wasEnabled = existing.Enabled;
            var hasCache = LocalDriveCacheLocator.HasCache(indexCacheDir, drive);
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

        var shouldBuild = isPresent && isEnabled && !LocalDriveCacheLocator.HasCache(indexCacheDir, drive);
        if (shouldBuild)
            drivesToBuild.Add(drive);
        return new UsnIndexer.DriveIndexStatus
        {
            Drive = drive,
            Enabled = isPresent && isEnabled,
            Kind = isPresent ? VolumeHelper.GetDisplayFileSystemType(drive) : "-",
            State = shouldBuild ? "pending" : isPresent && isEnabled ? "ready" : isPresent ? "disabled" : "unavailable",
            CachePath = isPresent ? LocalDriveCacheLocator.GetCachePath(indexCacheDir, drive) : string.Empty
        };
    }
}
