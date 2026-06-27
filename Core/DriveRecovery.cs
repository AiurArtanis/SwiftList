using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core;

internal static class DriveRecovery
{
    public static void RestoreOrRebuild(
        UsnIndexer indexer,
        string cacheDir,
        string drive,
        CancellationToken token,
        Action<string>? onReindexRequired,
        Action<IDisposable> addMonitor)
    {
        Logger.Log($"[SearchEngine] Restoring newly available drive {drive} from cache if possible.");
        var cached = indexer.TryLoadDriveFromCache(cacheDir, drive);
        if (cached.HasValue)
        {
            if (!SupportsJournal(drive))
            {
                TrySaveDriveCache(indexer, cacheDir, new() { (drive, cached.Value.JournalId, cached.Value.NextUsn) }, drive, "folder restore");
                StartFolderMonitor(indexer, drive, onReindexRequired, addMonitor, token);
                Logger.Log($"[SearchEngine] Restored folder-scan drive {drive} from cache.");
                return;
            }

            var nextUsn = indexer.CatchUpDrive(drive, cached.Value.JournalId, cached.Value.NextUsn);
            if (nextUsn >= 0)
            {
                TrySaveDriveCache(indexer, cacheDir, new() { (drive, cached.Value.JournalId, nextUsn) }, drive, "USN catch-up");
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

        TrySaveDriveCache(indexer, cacheDir, metadata, drive, "drive rebuild");
        foreach (var (builtDrive, journalId, nextUsn) in metadata)
        {
            if (SupportsJournal(builtDrive))
                new UsnMonitor(builtDrive, journalId, nextUsn, indexer, token, onReindexRequired).Start();
            else
                StartFolderMonitor(indexer, builtDrive, onReindexRequired, addMonitor, token);
        }
    }

    private static bool SupportsJournal(string drive)
    {
        var fs = VolumeHelper.GetFileSystemType(drive);
        return fs.Equals("NTFS", StringComparison.OrdinalIgnoreCase) || fs.Equals("ReFS", StringComparison.OrdinalIgnoreCase);
    }

    private static void StartFolderMonitor(UsnIndexer indexer, string drive, Action<string>? onReindexRequired, Action<IDisposable> addMonitor, CancellationToken token)
    {
        var monitor = new FolderDriveMonitor(drive, (changeType, path, oldPath) => indexer.ApplyFolderChange(drive, changeType, path, oldPath), token);
        monitor.Start();
        addMonitor(monitor);
    }

    private static void TrySaveDriveCache(
        UsnIndexer indexer,
        string cacheDir,
        List<(string Drive, ulong JournalId, long NextUsn)> metadata,
        string drive,
        string stage)
    {
        try
        {
            indexer.SaveDrivesToCache(cacheDir, metadata);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchEngine] Failed to save cache for drive {drive} after {stage}: {ex.Message}", LogLevel.Warn);
        }
    }
}
