using SwiftList.Core.IndexV2;

namespace SwiftList.Core.Indexer.Usn;

internal static class LocalDriveCacheLocator
{
    public static string GetCachePath(string cacheDir, string drive) => FileRecordStoreSerializer.GetBasePath(cacheDir, GetRequiredCacheKey(drive)) + ".idx";

    public static bool HasCache(string cacheDir, string drive)
    {
        var key = GetCacheKey(drive);
        return key != null && File.Exists(FileRecordStoreSerializer.GetBasePath(cacheDir, key) + ".idx");
    }

    public static void Delete(string cacheDir, string drive)
    {
        var key = GetCacheKey(drive);
        if (key != null)
            TryDelete(FileRecordStoreSerializer.GetBasePath(cacheDir, key) + ".idx");
    }

    // Drives with an on-disk cache but not currently detected (unplugged, disconnected) still need a
    // status row -- otherwise they'd vanish from the list entirely instead of showing as "unavailable".
    public static IReadOnlyList<string> ListCachedDrives(string cacheDir)
    {
        if (!Directory.Exists(cacheDir))
            return Array.Empty<string>();

        var drives = new List<string>();
        foreach (var path in Directory.EnumerateFiles(cacheDir, "*.idx"))
        {
            SnapshotFormat.Meta? meta;
            try
            {
                meta = SnapshotFormat.TryReadHeaderFromFile(path);
            }
            catch (IOException)
            {
                continue; // mid-write, not corruption -- picked up again next refresh
            }

            if (meta == null)
            {
                TryDelete(path);
                continue;
            }

            if (meta.SourceKind == FileRecordSourceKind.LocalMft)
            {
                var drive = NormalizeDrive(meta.SourceKey);
                if (drive.Length == 1)
                    drives.Add(drive);
            }
        }
        return drives.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
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
