namespace SwiftList.Core;

internal sealed class FileRecordNamePool
{
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _pool = new(StringComparer.Ordinal);

    public string Get(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        lock (_lock)
        {
            if (_pool.TryGetValue(value, out var pooled))
                return pooled;

            _pool[value] = value;
            return value;
        }
    }
}

public static class FileRecordStoreSerializer
{
    internal const string MetaMagic = "SLRCMETA";
    // v9: $MFT-based one-to-many hard-link index. Bumping this invalidates older single-name caches
    // so existing installs rebuild once and pick up full hard-link paths.
    // v10: force one rebuild to purge records orphaned by incremental USN updates (parent collapsed to a
    // self-parent, shown under the drive root). New caches keep the true parent FRN so it can't recur.
    // v11: force one rebuild to purge children of deleted directories that were never cascade-removed
    // (HardLinkDelta.RemoveLink only marked the directory's own row deleted). Fixed going forward, but
    // existing caches already have the orphaned rows baked in and won't self-heal without a rebuild.
    // v12: records gained Size and Creation/LastWrite/LastAccess timestamps (the latter as a 4-byte
    // whole-second Unix time via FileTimeHelper, not the native 8-byte FILETIME -- a search tool has no
    // use for sub-second precision, and it roughly halves the timestamps' footprint across millions of
    // rows). Existing caches don't carry this data at all, so force one rebuild to populate it instead of
    // leaving old rows permanently zeroed.
    // v13: fixed incremental USN/watcher updates leaving Size/timestamps at zero for a file created and
    // left empty (no data ever written) or a plain rename/move (no other attribute change) -- see
    // UsnIndexerExtensions.MetadataRefreshReasons. Caches built before this fix can have real files stuck
    // at CreatedUtc=0, which GetRecentFiles reads as "created at the Unix epoch" and filters out of every
    // age-windowed query; force one rebuild so the full FileInfo-based walk (which always stats correctly)
    // repopulates them. Shared by both the local-drive cache (LocalDriveCacheLocator) and the network/WSL
    // cache (NetworkDriveCacheLocator), so both get swept.
    // v14: added FileRecordStore.IsComplete (meta) and FileRecordFlags.Listed (per directory record), for
    // resumable network/WSL drive scans (TreeDiffBaseline) -- force one rebuild since older caches have
    // neither bit and would otherwise look like a directory that was discovered but never actually listed.
    // v15: added FileRecordStore.ExclusionRulesFingerprint (meta), so a resumed network/WSL/folder scan can
    // tell whether exclusion rules changed since this store was produced without any external signal. Older
    // caches were never stamped with one at all, so force one rebuild instead of guessing.
    public const int Version = 15;

    public static string GetBasePath(string cacheDir, string sourceKey) => Path.Combine(cacheDir, sourceKey.ToLowerInvariant());

    public static bool Exists(string cacheDir, string sourceKey)
    {
        var basePath = GetBasePath(cacheDir, sourceKey);
        return ExistsBasePath(basePath);
    }

    public static bool ExistsBasePath(string basePath) => File.Exists(basePath + ".meta") &&
                                                          File.Exists(basePath + ".records") &&
                                                          File.Exists(basePath + ".names");

    public static void Delete(string cacheDir, string sourceKey) => DeleteBasePath(GetBasePath(cacheDir, sourceKey));

    public static void DeleteBasePath(string basePath)
    {
        TryDelete(basePath + ".meta");
        TryDelete(basePath + ".records");
        TryDelete(basePath + ".names");
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
}
