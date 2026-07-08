namespace SwiftList.Core.Indexer.Usn;

internal static class LocalDriveCacheLocator
{
    public static string GetCachePath(string cacheDir, string drive)
        => FileRecordStoreSerializer.GetBasePath(cacheDir, GetRequiredCacheKey(drive)) + ".meta";

    public static bool HasCache(string cacheDir, string drive)
    {
        var key = GetCacheKey(drive);
        return key != null && FileRecordStoreSerializer.Exists(cacheDir, key);
    }

    public static FileRecordStoreSummary? TryLoadSummary(string cacheDir, string drive)
    {
        var key = GetCacheKey(drive);
        if (key == null) return null;

        FileRecordStoreSummary? summary;
        try
        {
            summary = FileRecordStoreSerializer.LoadSummary(cacheDir, key);
        }
        catch (IOException)
        {
            // Busy right now (e.g. a save mid-write) -- not evidence of corruption, don't delete.
            return null;
        }

        if (!summary.HasValue)
        {
            FileRecordStoreSerializer.Delete(cacheDir, key);
            return null;
        }
        return summary;
    }

    public static void Save(string cacheDir, string drive, FileRecordStore store)
        => FileRecordStoreSerializer.Save(cacheDir, store, GetRequiredCacheKey(drive));

    public static void Delete(string cacheDir, string drive)
    {
        var key = GetCacheKey(drive);
        if (key != null)
            FileRecordStoreSerializer.Delete(cacheDir, key);
    }

    public static IReadOnlyList<string> ListCachedDrives(string cacheDir)
    {
        if (!Directory.Exists(cacheDir))
            return Array.Empty<string>();

        var drives = new List<string>();
        foreach (var path in Directory.EnumerateFiles(cacheDir, "*.meta"))
        {
            var key = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            FileRecordStoreSummary? summary;
            try
            {
                summary = FileRecordStoreSerializer.LoadSummary(cacheDir, key);
            }
            catch (IOException)
            {
                continue;
            }

            if (!summary.HasValue)
            {
                FileRecordStoreSerializer.Delete(cacheDir, key);
                continue;
            }

            if (summary.Value.SourceKind == FileRecordSourceKind.LocalMft)
            {
                var drive = NormalizeDrive(summary.Value.SourceKey);
                if (drive.Length == 1)
                    drives.Add(drive);
            }
        }
        return drives.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string GetRequiredCacheKey(string drive)
        => GetCacheKey(drive) ?? throw new InvalidOperationException($"Volume identity unavailable for drive {drive}.");

    private static string? GetCacheKey(string drive)
    {
        var identity = VolumeHelper.GetVolumeIdentity(drive);
        return identity.HasValue ? VolumeHelper.GetVolumeCacheKey(identity.Value) : null;
    }

    private static string NormalizeDrive(string drive) => string.IsNullOrWhiteSpace(drive)
        ? string.Empty
        : drive.Trim().TrimEnd(':', '\\').ToUpperInvariant();
}
