using System.Diagnostics;

namespace SwiftList.Core.Indexer.Usn;

internal static class IndexBuilder
{
    public static List<(string Drive, ulong JournalId, long NextUsn)> BuildDrives(
        JournalReader reader,
        IReadOnlyList<string> drives,
        Action<string, string> setDriveState,
        Action<string, UInt128, Dictionary<UInt128, (string Name, UInt128 ParentFrn, bool IsDir)>, long, ulong, int, int> onDriveCompleted,
        Action<double> onCompleted)
    {
        var stopWatch = Stopwatch.StartNew();
        var monitorsToStart = new List<(string Drive, ulong JournalId, long NextUsn)>();

        var indexResults = new (string Drive, (UInt128 RootFrn,
                                Dictionary<UInt128, (string Name, UInt128 ParentFrn, bool IsDir)> SearchItems,
                                long NextUsn, ulong JournalId)? Result)[drives.Count];

        Parallel.For(0, drives.Count, i =>
        {
            var drive = drives[i];
            Logger.Log($"[UsnIndexer] Indexing drive {drive} in parallel ({i + 1}/{drives.Count})");
            indexResults[i] = (drive, reader.IndexDrive(drive));
        });

        for (var i = 0; i < drives.Count; i++)
        {
            var (drive, res) = indexResults[i];
            if (res.HasValue)
            {
                var data = res.Value;
                Logger.Log($"[UsnIndexer] Drive {drive} indexing completed. Found {data.SearchItems.Count} items.");
                setDriveState(drive, "indexing");

                var progress = (int)(((double)(i + 1) / drives.Count) * 100);
                onDriveCompleted(drive, data.RootFrn, data.SearchItems, data.NextUsn, data.JournalId, progress, i + 1);

                monitorsToStart.Add((drive, data.JournalId, data.NextUsn));
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
