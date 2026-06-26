using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.Indexer.Usn;

public static class UsnIndexerBuildExtensions
{
    public static List<(string Drive, ulong JournalId, long NextUsn)> BuildIndex(this UsnIndexer indexer)
    {
        Logger.Log("[UsnIndexer] BuildIndex started");
        var drives = VolumeHelper.DetectSupportedDrives();
        Logger.Log($"[UsnIndexer] Detected NTFS/ReFS drives: {string.Join(", ", drives)}");
        return indexer.BuildDrives(drives, clearExisting: true);
    }

    public static List<(string Drive, ulong JournalId, long NextUsn)> BuildDrives(
        this UsnIndexer indexer,
        IReadOnlyList<string> drives,
        bool clearExisting)
    {
        lock (indexer.LockObj)
        {
            if (clearExisting)
            {
                indexer.Status.State = "indexing";
                indexer.Status.Progress = 0;
            }
            if (clearExisting)
            {
                indexer.Status.TotalFiles = 0;
                indexer.Status.TotalDirs = 0;
                indexer._driveMetadata.Clear();
                indexer._recordIndexes.Clear();
                indexer.Status.ActiveDrives.Clear();
            }

            if (clearExisting)
                indexer.Status.ActiveDrives = drives.ToList();
            else
            {
                foreach (var drive in drives)
                {
                    if (!indexer.Status.ActiveDrives.Contains(drive))
                        indexer.Status.ActiveDrives.Add(drive);
                    indexer.SetDriveState(drive, "indexing");
                }
            }
        }

        return IndexBuilder.BuildDrives(
            indexer._reader,
            drives,
            indexer.SetDriveState,
            (drive, rootFrn, searchItems, nextUsn, journalId, progress, index) =>
            {
                lock (indexer.LockObj)
                {
                    var store = IndexCacheManager.CreateStoreFromDriveData(drive, rootFrn, searchItems, nextUsn, journalId);

                    var runtime = new RuntimeIndex();
                    runtime.Load(store);
                    indexer._driveMetadata[drive] = UsnIndexer.CreateMetadata(store);
                    indexer._recordIndexes[drive] = runtime;
                    indexer.Status.TotalFiles = indexer._recordIndexes.Values.Sum(r => r.TotalFiles);
                    indexer.Status.TotalDirs = indexer._recordIndexes.Values.Sum(r => r.TotalDirs);

                    indexer.Status.Progress = progress;
                    indexer.UpdateDriveCounts(drive);
                }
            },
            elapsedSeconds =>
            {
                lock (indexer.LockObj)
                {
                    indexer.Status.State = "ready";
                    indexer.Status.Progress = 100;
                    indexer.Status.ElapsedTime = elapsedSeconds;
                }
                Logger.Log($"[UsnIndexer] All indices built! Files: {indexer.Status.TotalFiles}, Folders: {indexer.Status.TotalDirs}. Time: {elapsedSeconds:F2}s");
            }
        );
    }
}
