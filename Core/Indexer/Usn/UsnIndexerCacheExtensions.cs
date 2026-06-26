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

            var metadata = new List<(string Drive, ulong JournalId, long NextUsn)>();
            foreach (var drive in drives)
            {
                var store = FileRecordStoreSerializer.Load(cacheDir, drive);
                if (store != null)
                {
                    var runtime = new RuntimeIndex();
                    runtime.Load(store);
                    indexer._driveMetadata[drive] = UsnIndexer.CreateMetadata(store);
                    indexer._recordIndexes[drive] = runtime;
                    indexer.Status.TotalFiles += runtime.TotalFiles;
                    indexer.Status.TotalDirs += runtime.TotalDirs;
                    indexer.Status.ActiveDrives.Add(drive);
                    metadata.Add((drive, store.JournalId, store.NextUsn));
                    indexer.UpdateDriveCounts(drive);
                }
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
        lock (indexer.LockObj)
        {
            var store = FileRecordStoreSerializer.Load(cacheDir, drive);
            if (store == null)
                return null;

            var runtime = new RuntimeIndex();
            runtime.Load(store);
            indexer._driveMetadata[drive] = UsnIndexer.CreateMetadata(store);
            indexer._recordIndexes[drive] = runtime;

            if (!indexer.Status.ActiveDrives.Contains(drive, StringComparer.OrdinalIgnoreCase))
                indexer.Status.ActiveDrives.Add(drive);

            indexer.Status.TotalFiles = indexer._recordIndexes.Values.Sum(r => r.TotalFiles);
            indexer.Status.TotalDirs = indexer._recordIndexes.Values.Sum(r => r.TotalDirs);
            indexer.Status.State = "ready";
            indexer.Status.Progress = 100;
            indexer.UpdateDriveCounts(drive);
            return (store.JournalId, store.NextUsn);
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
}
