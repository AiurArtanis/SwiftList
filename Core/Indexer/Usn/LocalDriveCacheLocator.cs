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

    public static FileRecordStore? Load(string cacheDir, string drive)
    {
        var key = GetCacheKey(drive);
        if (key == null)
            return null;

        var store = FileRecordStoreSerializer.Load(cacheDir, key);
        if (store == null)
            return null;

        store.SourceKey = drive;
        return store;
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

        return Directory.EnumerateFiles(cacheDir, "*.meta")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Select(key => FileRecordStoreSerializer.Load(cacheDir, key))
            .Where(store => store?.SourceKind == FileRecordSourceKind.LocalMft)
            .Select(store => NormalizeDrive(store!.SourceKey))
            .Where(drive => drive.Length == 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(drive => drive, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
