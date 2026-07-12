namespace SwiftList.Core;

internal static class FileRecordStoreReplaceHelper
{
    // No backup path -- File.Replace(..., destinationBackupFileName: null, ...) skips creating one
    // entirely. The old snapshot was never actually meant to survive as a real backup (nothing ever
    // reads one back), it was only ever a byproduct of File.Replace's own API shape, immediately
    // deleted right after -- and that delete could fail if the old snapshot was still memory-mapped by
    // an active Snapshot reader at that exact moment, leaving a stale "<name>.idx.bak" nothing would
    // ever clean up (see SearchEngineInitializer.CleanupStaleBackupsIn, which sweeps up any left behind
    // by an older build of this method). Passing null sidesteps the whole problem at the source.
    public static void ReplaceWithRetry(string tempPath, string finalPath)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (File.Exists(finalPath))
                    File.Replace(tempPath, finalPath, null, ignoreMetadataErrors: true);
                else
                    File.Move(tempPath, finalPath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }

        if (File.Exists(finalPath))
            File.Replace(tempPath, finalPath, null, ignoreMetadataErrors: true);
        else
            File.Move(tempPath, finalPath, overwrite: true);
    }
}
