using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.Indexer.Usn;

public static class UsnIndexerCacheExtensions
{
    public static void SaveDrivesToCache(
        this UsnIndexer indexer,
        string cacheDir,
        List<(string Drive, ulong JournalId, long NextUsn)> driveMetadata)
    {
        lock (indexer.LockObj)
        {
            IndexCacheManager.SaveDrivesToCache(cacheDir, driveMetadata, indexer._recordIndexes, indexer._driveMetadata);
        }
    }

    public static List<(string Drive, ulong JournalId, long NextUsn)> LoadDrivesFromCache(
        this UsnIndexer indexer,
        string cacheDir,
        IReadOnlyList<string> drives)
    {
        lock (indexer.LockObj)
        {
            indexer._driveMetadata.Clear();
            indexer._recordIndexes.Clear();
            indexer.Status.ActiveDrives.Clear();
            indexer.Status.TotalFiles = 0;
            indexer.Status.TotalDirs = 0;
        }

        var loaded = new List<(string Drive, RuntimeIndex Runtime, UsnIndexer.DriveRuntimeMetadata Metadata, ulong JournalId, long NextUsn)>();
        foreach (var drive in drives)
        {
            var runtime = new RuntimeIndex();
            string? basePath = null;
            try
            {
                var metaPath = LocalDriveCacheLocator.GetCachePath(cacheDir, drive);
                if (!string.IsNullOrEmpty(metaPath) && metaPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    basePath = metaPath.Substring(0, metaPath.Length - 5);
                }
            }
            catch {}

            if (basePath == null)
                continue;

            var metadata = runtime.LoadFromCacheDirect(basePath);
            if (metadata == null || !IsCurrentVolumeCache(drive, metadata))
                continue;

            loaded.Add((drive, runtime, metadata, metadata.JournalId, metadata.NextUsn));

            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            Win32Api.TrimWorkingSet();
        }

        lock (indexer.LockObj)
        {
            var metadata = new List<(string Drive, ulong JournalId, long NextUsn)>();
            foreach (var item in loaded)
            {
                indexer._driveMetadata[item.Drive] = item.Metadata;
                indexer._recordIndexes[item.Drive] = item.Runtime;
                indexer.Status.TotalFiles += item.Runtime.TotalFiles;
                indexer.Status.TotalDirs += item.Runtime.TotalDirs;
                indexer.Status.ActiveDrives.Add(item.Drive);
                metadata.Add((item.Drive, item.JournalId, item.NextUsn));
                indexer.UpdateDriveCounts(item.Drive);
            }

            if (metadata.Count > 0)
            {
                indexer.Status.State = "ready";
                indexer.Status.Progress = 100;
            }

            indexer.CompactMemory();
            return metadata;
        }
    }

    public static (ulong JournalId, long NextUsn)? TryLoadDriveFromCache(
        this UsnIndexer indexer,
        string cacheDir,
        string drive)
    {
        var runtime = new RuntimeIndex();
        string? basePath = null;
        try
        {
            var metaPath = LocalDriveCacheLocator.GetCachePath(cacheDir, drive);
            if (!string.IsNullOrEmpty(metaPath) && metaPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                basePath = metaPath.Substring(0, metaPath.Length - 5);
            }
        }
        catch {}

        if (basePath == null)
            return null;

        var metadata = runtime.LoadFromCacheDirect(basePath);
        if (metadata == null || !IsCurrentVolumeCache(drive, metadata))
            return null;

        lock (indexer.LockObj)
        {
            indexer._driveMetadata[drive] = metadata;
            indexer._recordIndexes[drive] = runtime;

            if (!indexer.Status.ActiveDrives.Contains(drive, StringComparer.OrdinalIgnoreCase))
                indexer.Status.ActiveDrives.Add(drive);

            indexer.Status.TotalFiles = indexer._recordIndexes.Values.Sum(r => r.TotalFiles);
            indexer.Status.TotalDirs = indexer._recordIndexes.Values.Sum(r => r.TotalDirs);
            indexer.Status.State = "ready";
            indexer.Status.Progress = 100;
            indexer.UpdateDriveCounts(drive);
            return (metadata.JournalId, metadata.NextUsn);
        }
    }

    public static void DropDriveFromRuntime(this UsnIndexer indexer, string drive)
    {
        lock (indexer.LockObj)
        {
            indexer._driveMetadata.Remove(drive);
            indexer._recordIndexes.Remove(drive);
            indexer.Status.ActiveDrives.RemoveAll(d => d.Equals(drive, StringComparison.OrdinalIgnoreCase));
            indexer.Status.TotalFiles = indexer._recordIndexes.Values.Sum(r => r.TotalFiles);
            indexer.Status.TotalDirs = indexer._recordIndexes.Values.Sum(r => r.TotalDirs);
        }
    }

    private static bool IsCurrentVolumeCache(string drive, UsnIndexer.DriveRuntimeMetadata metadata)
    {
        if (metadata.SourceKind != FileRecordSourceKind.LocalMft)
            return true;

        var identity = VolumeHelper.GetVolumeIdentity(drive);
        if (!identity.HasValue)
        {
            Logger.Log($"[UsnIndexer] Ignoring cached index for drive {drive}: volume identity unavailable.", LogLevel.Warn);
            return false;
        }

        var current = identity.Value;
        var matches = metadata.VolumeSerialNumber == current.SerialNumber &&
            metadata.FileSystemType.Equals(current.FileSystemType, StringComparison.OrdinalIgnoreCase);
        if (!matches)
        {
            Logger.Log($"[UsnIndexer] Ignoring cached index for drive {drive}: volume identity changed.", LogLevel.Warn);
        }

        return matches;
    }
}
