using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core;

internal static class DriveRecovery
{
    public static void RestoreOrRebuild(
        UsnIndexer indexer,
        string cacheDir,
        string drive,
        CancellationToken token,
        Action<string>? onReindexRequired)
    {
        Logger.Log($"[SearchEngine] Restoring newly available drive {drive} from cache if possible.");
        var cached = indexer.TryLoadDriveFromCache(cacheDir, drive);
        if (cached.HasValue)
        {
            var nextUsn = indexer.CatchUpDrive(drive, cached.Value.JournalId, cached.Value.NextUsn);
            if (nextUsn >= 0)
            {
                indexer.SaveDrivesToCache(cacheDir, new() { (drive, cached.Value.JournalId, nextUsn) });
                new UsnMonitor(drive, cached.Value.JournalId, nextUsn, indexer, token, onReindexRequired).Start();
                Logger.Log($"[SearchEngine] Restored drive {drive} from cache and USN catch-up.");
                return;
            }
        }

        if (cached.HasValue)
            indexer.DropDriveFromRuntime(drive);

        Logger.Log($"[SearchEngine] Cache restore unavailable for drive {drive}; rebuilding this drive only.");
        var metadata = indexer.BuildDrives(new[] { drive }, clearExisting: false);
        if (metadata.Count == 0)
        {
            indexer.SetDriveState(drive, "failed");
            return;
        }

        indexer.SaveDrivesToCache(cacheDir, metadata);
        foreach (var (builtDrive, journalId, nextUsn) in metadata)
            new UsnMonitor(builtDrive, journalId, nextUsn, indexer, token, onReindexRequired).Start();
    }
}
