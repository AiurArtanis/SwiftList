namespace SwiftList.Core;

internal static class FileRecordStoreReplaceHelper
{
    public static void ReplaceWithRetry(string tempPath, string finalPath, Action<string> tryDelete)
    {
        const int maxAttempts = 5;
        var backupPath = finalPath + ".bak";
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (File.Exists(finalPath))
                {
                    File.Replace(tempPath, finalPath, backupPath, ignoreMetadataErrors: true);
                    tryDelete(backupPath);
                }
                else
                {
                    File.Move(tempPath, finalPath, overwrite: true);
                }
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }

        if (File.Exists(finalPath))
            File.Replace(tempPath, finalPath, backupPath, ignoreMetadataErrors: true);
        else
            File.Move(tempPath, finalPath, overwrite: true);
    }
}
