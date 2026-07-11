using System.Security.Cryptography;
using System.Text;
using SwiftList.Core.IndexV2;

namespace SwiftList.Core.Indexer.NetworkDrive;

internal static class NetworkDriveCacheLocator
{
    public static string GetCachePath(string drive)
        => FileRecordStoreSerializer.GetBasePath(Path.Combine(Logger.UserDataDir, "indexes"), GetStorageKeyOrFallback(drive)) + ".meta";

    // IndexV2's single-file snapshot, keyed by the same UNC/fallback identity as the legacy trio.
    public static string GetV2Path(string drive)
        => FileRecordStoreSerializer.GetBasePath(Path.Combine(Logger.UserDataDir, "indexes"), GetStorageKeyOrFallback(drive)) + ".idx";

    public static bool HasCache(string drive)
    {
        var key = TryResolveStorageKey(drive);
        if (key == null)
            return false;
        var cacheDir = Path.Combine(Logger.UserDataDir, "indexes");
        return File.Exists(FileRecordStoreSerializer.GetBasePath(cacheDir, key) + ".idx") || FileRecordStoreSerializer.Exists(cacheDir, key);
    }

    public static IReadOnlyList<string> GetCachedDrives()
    {
        var resolvedByUnc = NetworkDriveResolver.GetNetworkDrives()
            .Where(d => !string.IsNullOrWhiteSpace(d.UncPath))
            .ToDictionary(d => NormalizeUnc(d.UncPath), d => d.Letter, StringComparer.OrdinalIgnoreCase);

        // Was filtered to single-letter drives only, which silently made this never return UNC/WSL/
        // folder-index keys -- e.g. the App's own "cached but currently unchecked WSL row" logic
        // (NetworkDriveSettingsViewModel filtering this list for entries starting with "\\") was dead
        // code, always empty. Broadened to return every distinct normalized key regardless of shape.
        return EnumerateNetworkStores()
            .Select(store =>
            {
                var unc = NormalizeUnc(store.FileSystemType);
                return unc.Length > 0 && resolvedByUnc.TryGetValue(unc, out var currentDrive)
                    ? currentDrive
                    : IndexerHelper.NormalizeDrive(store.SourceKey);
            })
            .Where(drive => !string.IsNullOrEmpty(drive))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(drive => drive, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void DeleteCache(string drive)
    {
        var storageKey = TryResolveStorageKey(drive);
        if (storageKey == null)
            return;
        var cacheDir = Path.Combine(Logger.UserDataDir, "indexes");
        TryDelete(FileRecordStoreSerializer.GetBasePath(cacheDir, storageKey) + ".idx");
        FileRecordStoreSerializer.Delete(cacheDir, storageKey);
    }

    public static bool TryLoad(string drive, out NetworkIndex index)
    {
        index = new NetworkIndex(drive);
        var storageKey = TryResolveStorageKey(drive);
        if (storageKey == null)
            return false;

        var cacheDir = Path.Combine(Logger.UserDataDir, "indexes");
        var v2Path = FileRecordStoreSerializer.GetBasePath(cacheDir, storageKey) + ".idx";
        if (!File.Exists(v2Path))
            return false; // no legacy-cache migration -- caller does a full fresh rebuild instead

        try
        {
            index = NetworkIndex.FromSnapshotFile(IndexerHelper.NormalizeDrive(drive), v2Path);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkDriveCacheLocator] Failed to open IndexV2 cache for {drive}: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    public static void Save(NetworkIndex index) => index.SaveToCache(GetV2Path(index.Drive));

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

        var cacheDir = Path.Combine(Logger.UserDataDir, "indexes");

        // A folder-index target has no UNC to resolve, ever -- GetStorageKeyOrFallback saved it under
        // BuildFallbackStorageKey(drive) directly. Check that exact key (either format) before falling
        // through to the FileSystemType-based scan below, which for a folder index is empty (never a
        // real UNC) and would resolve to a filename nothing was ever saved under, silently orphaning
        // the cache on delete/reload.
        var fallbackKey = BuildFallbackStorageKey(normalizedDrive);
        if (File.Exists(FileRecordStoreSerializer.GetBasePath(cacheDir, fallbackKey) + ".idx") || FileRecordStoreSerializer.Exists(cacheDir, fallbackKey))
            return fallbackKey;

        // Last resort for a drive/share that WAS connected when saved (so its cache's FileSystemType holds
        // the real UNC it was keyed under) but is disconnected right now.
        var fallback = EnumerateNetworkStores()
            .FirstOrDefault(store => store.SourceKey.TrimEnd(':')
                .Equals(normalizedDrive.TrimEnd(':'), StringComparison.OrdinalIgnoreCase));
        return fallback.SourceKey == null ? null : BuildStorageKey(fallback.FileSystemType);
    }

    // Unifies legacy .meta and IndexV2 .idx summaries under one shape for discovery/fallback-key scans
    // -- both formats carry the same fields, just via different readers.
    private readonly record struct StoreSummary(string SourceKey, string FileSystemType, FileRecordSourceKind SourceKind);

    private static IEnumerable<StoreSummary> EnumerateNetworkStores()
    {
        var cacheDir = Path.Combine(Logger.UserDataDir, "indexes");
        if (!Directory.Exists(cacheDir))
            yield break;

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(cacheDir, "*.idx"))
        {
            var storageKey = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(storageKey) || !seenKeys.Add(storageKey))
                continue;

            SnapshotFormat.Meta? meta;
            try
            {
                meta = SnapshotFormat.TryReadHeaderFromFile(path);
            }
            catch (IOException)
            {
                continue; // mid-write, not corruption -- picked up again next enumeration
            }

            if (meta == null)
            {
                TryDelete(path);
                continue;
            }
            if (meta.SourceKind == FileRecordSourceKind.NetworkMappedDrive)
                yield return new StoreSummary(meta.SourceKey, meta.FileSystemType, meta.SourceKind);
        }

        foreach (var path in Directory.EnumerateFiles(cacheDir, "*.meta"))
        {
            var storageKey = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(storageKey) || !seenKeys.Add(storageKey))
                continue;

            FileRecordStoreSummary? summary;
            try
            {
                summary = FileRecordStoreSummaryLoader.LoadSummary(cacheDir, storageKey);
            }
            catch (IOException)
            {
                continue;
            }

            if (!summary.HasValue)
            {
                FileRecordStoreSerializer.Delete(cacheDir, storageKey);
                continue;
            }

            var val = summary.Value;
            if (val.SourceKind == FileRecordSourceKind.NetworkMappedDrive)
                yield return new StoreSummary(val.SourceKey, val.FileSystemType, val.SourceKind);
        }
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
