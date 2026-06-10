using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.Indexer.Usn;

public static class UsnIndexerCacheExtensions
{
    public static void SaveDrivesToCache(
        this UsnIndexer indexer,
        string cacheDir,
        List<(string Drive, ulong JournalId, long NextUsn)> driveMetadata) => IndexCacheManager.SaveDrivesToCache(cacheDir, driveMetadata, indexer._recordIndexes, indexer._driveMetadata);

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
}
