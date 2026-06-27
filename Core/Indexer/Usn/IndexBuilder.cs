using System.Diagnostics;

namespace SwiftList.Core.Indexer.Usn;

internal static class IndexBuilder
{
    public static List<(string Drive, ulong JournalId, long NextUsn)> BuildDrives(
        JournalReader reader,
        IReadOnlyList<string> drives,
        Action<string, string> setDriveState,
        Action<string, int, int> onDriveProgress,
        Func<string, Action<int, int>?, FolderDriveScanner.FolderDriveBuildResult?> buildFolderDrive,
        Action<string, FolderDriveScanner.FolderDriveBuildResult, int, int> onFolderDriveCompleted,
        Action<string, UsnDriveIndexResult, int, int> onDriveCompleted,
        Action<double> onCompleted)
    {
        var stopWatch = Stopwatch.StartNew();
        var monitorsToStart = new List<(string Drive, ulong JournalId, long NextUsn)>();

        var indexResults = new (string Drive, UsnDriveIndexResult? Result)[drives.Count];
        var folderResults = new (string Drive, FolderDriveScanner.FolderDriveBuildResult? Result)[drives.Count];

        Parallel.For(0, drives.Count, i =>
        {
            var drive = drives[i];
            Logger.Log($"[UsnIndexer] Indexing drive {drive} in parallel ({i + 1}/{drives.Count})");
            var fs = VolumeHelper.GetFileSystemType(drive);
            if (fs.Equals("NTFS", StringComparison.OrdinalIgnoreCase) || fs.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
                indexResults[i] = (drive, reader.IndexDrive(drive, (files, dirs) => onDriveProgress(drive, files, dirs)));
            else
                folderResults[i] = (drive, buildFolderDrive(drive, (files, dirs) => onDriveProgress(drive, files, dirs)));
        });

        for (var i = 0; i < drives.Count; i++)
        {
            var drive = drives[i];
            var res = indexResults[i].Result;
            if (res != null)
            {
                var data = res;
                Logger.Log($"[UsnIndexer] Drive {drive} indexing completed. Found {data.ItemCount} items.");
                setDriveState(drive, "indexing");

                var progress = (int)(((double)(i + 1) / drives.Count) * 100);
                onDriveCompleted(drive, data, progress, i + 1);

                monitorsToStart.Add((drive, data.JournalId, data.NextUsn));
            }
            else if (folderResults[i].Result.HasValue)
            {
                var result = folderResults[i].Result!.Value;
                Logger.Log($"[UsnIndexer] Drive {drive} folder scan completed. Found {result.Store.Records.Count} items.");
                setDriveState(drive, "indexing");

                var progress = (int)(((double)(i + 1) / drives.Count) * 100);
                onFolderDriveCompleted(drive, result, progress, i + 1);
                monitorsToStart.Add((drive, 0, 0));
            }
            else
            {
                Logger.Log($"[UsnIndexer] Drive {drive} indexing failed.", LogLevel.Error);
            }
        }

        stopWatch.Stop();
        onCompleted(stopWatch.Elapsed.TotalSeconds);

        return monitorsToStart;
    }
}
