using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core.DriveMonitoring;

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
            if (!SupportsJournal(drive))
            {
                TrySaveDriveCache(indexer, cacheDir, new() { (drive, cached.Value.JournalId, cached.Value.NextUsn) }, drive, "folder restore");
                DriveMonitorFactory.EnsureMonitor(indexer, drive, cached.Value.JournalId, cached.Value.NextUsn, token, onReindexRequired);
                Logger.Log($"[SearchEngine] Restored folder-scan drive {drive} from cache.");
                return;
            }

            var nextUsn = indexer.CatchUpDrive(drive, cached.Value.JournalId, cached.Value.NextUsn);
            if (nextUsn >= 0)
            {
                TrySaveDriveCache(indexer, cacheDir, new() { (drive, cached.Value.JournalId, nextUsn) }, drive, "USN catch-up");
                DriveMonitorFactory.EnsureMonitor(indexer, drive, cached.Value.JournalId, nextUsn, token, onReindexRequired);
                Logger.Log($"[SearchEngine] Restored drive {drive} from cache and USN catch-up.");
                return;
            }
        }

        if (cached.HasValue)
            indexer.DropDriveFromRuntime(drive);

        Logger.Log($"[SearchEngine] Cache restore unavailable for drive {drive}; rebuilding this drive only.");
        // Stop this drive's own currently-running monitor (if any) before the rebuild starts -- see
        // UsnIndexer.RemoveDriveMonitor's own comment on why a still-running monitor over the rebuild
        // window can otherwise lose whatever it detects. Safe no-op if nothing is registered yet.
        indexer.RemoveDriveMonitor(drive);
        var metadata = indexer.BuildDrives(new[] { drive }, clearExisting: false, cacheDir: cacheDir);
        if (metadata.Count == 0)
        {
            indexer.SetDriveState(drive, "failed");
            return;
        }

        foreach (var (builtDrive, journalId, nextUsn) in metadata)
            DriveMonitorFactory.EnsureMonitor(indexer, builtDrive, journalId, nextUsn, token, onReindexRequired);
    }

    private static bool SupportsJournal(string drive)
    {
        var fs = VolumeHelper.GetFileSystemType(drive);
        return fs.Equals("NTFS", StringComparison.OrdinalIgnoreCase) || fs.Equals("ReFS", StringComparison.OrdinalIgnoreCase);
    }

    private static void TrySaveDriveCache(
        UsnIndexer indexer,
        string cacheDir,
        List<(string Drive, ulong JournalId, long NextUsn)> metadata,
        string drive,
        string stage)
    {
        if (metadata.Count == 0)
            return;
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
