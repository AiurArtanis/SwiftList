using System.Security.Cryptography;
using System.Text;

namespace SwiftList.Core.Indexer.NetworkDrive;

internal static class NetworkDriveCacheLocator
{
    public static string GetCachePath(string drive)
        => FileRecordStoreSerializer.GetBasePath(Path.Combine(Logger.UserDataDir, "indexes"), GetStorageKeyOrFallback(drive)) + ".meta";

    public static bool HasCache(string drive)
    {
        var key = TryResolveStorageKey(drive);
        return key != null && FileRecordStoreSerializer.Exists(Path.Combine(Logger.UserDataDir, "indexes"), key);
    }

    public static IReadOnlyList<string> GetCachedDrives()
    {
        var resolvedByUnc = NetworkDriveResolver.GetNetworkDrives()
            .Where(d => !string.IsNullOrWhiteSpace(d.UncPath))
            .ToDictionary(d => NormalizeUnc(d.UncPath), d => d.Letter, StringComparer.OrdinalIgnoreCase);

        return EnumerateNetworkStores()
            .Select(store =>
            {
                var unc = NormalizeUnc(store.FileSystemType);
                return unc.Length > 0 && resolvedByUnc.TryGetValue(unc, out var currentDrive)
                    ? currentDrive
                    : IndexerHelper.NormalizeDrive(store.SourceKey);
            })
            .Where(drive => drive.Length == 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(drive => drive, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void DeleteCache(string drive)
    {
        var storageKey = TryResolveStorageKey(drive);
        if (storageKey != null)
            FileRecordStoreSerializer.Delete(Path.Combine(Logger.UserDataDir, "indexes"), storageKey);
    }

    public static bool TryLoad(string drive, out NetworkIndex index)
    {
        index = new NetworkIndex(drive);
        var storageKey = TryResolveStorageKey(drive);
        if (storageKey == null)
            return false;

        var store = FileRecordStoreSerializer.Load(Path.Combine(Logger.UserDataDir, "indexes"), storageKey);
        if (store == null)
            return false;

        try
        {
            store.SourceKey = IndexerHelper.NormalizeDrive(drive);
            index = NetworkIndex.FromStore(store);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkDriveCacheLocator] Failed to load network drive {drive}: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    public static void Save(NetworkIndex index)
        => FileRecordStoreSerializer.Save(Path.Combine(Logger.UserDataDir, "indexes"), index.ToStore(), GetStorageKeyOrFallback(index.Drive));

    private static string GetStorageKeyOrFallback(string drive)
    {
        var unc = NetworkDriveResolver.GetUncPath(drive);
        return !string.IsNullOrWhiteSpace(unc)
            ? BuildStorageKey(unc)
            : BuildFallbackStorageKey(IndexerHelper.NormalizeDrive(drive));
    }

    private static string? TryResolveStorageKey(string drive)
    {
        var normalizedDrive = IndexerHelper.NormalizeDrive(drive);
        if (normalizedDrive.Length == 0)
            return null;

        var unc = NetworkDriveResolver.GetUncPath(normalizedDrive);
        if (!string.IsNullOrWhiteSpace(unc))
            return BuildStorageKey(unc);

        var fallback = EnumerateNetworkStores()
            .Cast<FileRecordStoreSummary?>()
            .FirstOrDefault(store => store.HasValue && store.Value.SourceKey.TrimEnd(':')
                .Equals(normalizedDrive.TrimEnd(':'), StringComparison.OrdinalIgnoreCase));
        return !fallback.HasValue ? null : BuildStorageKey(fallback.Value.FileSystemType);
    }

    private static IEnumerable<FileRecordStoreSummary> EnumerateNetworkStores()
    {
        var cacheDir = Path.Combine(Logger.UserDataDir, "indexes");
        if (!Directory.Exists(cacheDir))
            yield break;

        foreach (var path in Directory.EnumerateFiles(cacheDir, "*.meta"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var storageKey = name;
            FileRecordStoreSummary? summary;
            try
            {
                summary = FileRecordStoreSerializer.LoadSummary(cacheDir, storageKey);
            }
            catch (IOException)
            {
                // Busy right now -- most likely a checkpoint save mid-write for this exact drive. Skip this
                // pass without touching the file; it'll be picked up again next time this is enumerated.
                continue;
            }

            if (!summary.HasValue)
            {
                // Delete outdated or corrupted caches immediately to prevent infinite retry loops and CPU spikes
                FileRecordStoreSerializer.Delete(cacheDir, storageKey);
                continue;
            }

            var val = summary.Value;
            if (val.SourceKind == FileRecordSourceKind.NetworkMappedDrive)
                yield return val;
        }
    }

    public static string GetIdForUnc(string unc)
    {
        var normalized = NormalizeUnc(unc).ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string BuildStorageKey(string unc) => GetIdForUnc(unc);

    private static string BuildFallbackStorageKey(string drive)
    {
        var normalized = IndexerHelper.NormalizeDrive(drive).ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeUnc(string? unc)
        => string.IsNullOrWhiteSpace(unc)
            ? string.Empty
            : unc.Trim().TrimEnd('\\').Replace('/', '\\');
}
