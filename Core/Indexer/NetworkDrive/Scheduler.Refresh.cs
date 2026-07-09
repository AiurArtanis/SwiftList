using System.Diagnostics;

namespace SwiftList.Core.Indexer.NetworkDrive;

// The actual per-drive scan pass (RefreshDrive) and its resume-progress logging, split out of Scheduler.cs
// to keep it under the project's line limit.
internal sealed partial class Scheduler
{
    private void RefreshDrive(string drive, CancellationToken token)
    {
        // A bare letter needs ":\"; a UNC or folder-index path is already rooted as-is.
        var root = drive.Length == 1 ? drive + @":\" : drive;
        if (!root.EndsWith(Path.DirectorySeparatorChar.ToString()) && !root.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
        {
            root += Path.DirectorySeparatorChar;
        }
        var physicalRoot = root;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            _setStatus(drive, "indexing", 0, null);
            var settings = UserSettings.Load();
            var options = new WalkOptions(
                settings.ExcludedPaths,
                settings.IgnoredPathGlobs,
                settings.IgnoredPathRegexes,
                0,
                0,
                true);
            var previousStore = _getPreviousStore(drive);
            LogResumeProgress(drive, previousStore);
            var index = NetworkIndex.Build(
                drive,
                root,
                physicalRoot,
                options,
                token,
                // Fires every ~1024 items; unguarded, this can race past CancelDrive's "cached" revert
                // and clobber it back to "indexing" -- what made the Stop button lose that race sometimes.
                count => { if (!token.IsCancellationRequested) _setStatus(drive, "indexing", count, null); },
                (store, stats) => _onPublishCheckpoint(drive, store, stats, token),
                previousStore);
            token.ThrowIfCancellationRequested();
            // Reaching here without cancellation only means TreeBuilder.Run() drained its queue -- NOT
            // that every directory's real contents were captured. A directory that failed to enumerate
            // (network hiccup, permissions) is caught silently in WalkDirectory (CountError + return,
            // never MarkListed), so it correctly stays un-Listed for a future resume to retry -- but that
            // only works if this index isn't marked complete, since IsComplete=true tells Configure() this
            // drive needs no further refresh at all, which would permanently paper over the gap instead.
            index.IsComplete = index.Errors == 0;
            if (!index.IsComplete)
                Logger.Log($"[NetworkIndexer] {drive}: finished with {index.Errors} error(s) ({index.EnumerateErrors} enumerate, {index.AttributeErrors} attribute) -- not marking complete, next refresh will retry the gaps.", LogLevel.Warn);
            IndexerHelper.Save(index);

            stopwatch.Stop();
            Logger.Log($"[NetworkIndexer] {drive}: finished in {stopwatch.Elapsed.TotalSeconds:F1}s, {index.Count} records.");

            _onRefreshFinished(drive, index);
        }
        catch (OperationCanceledException)
        {
            Logger.Log($"[NetworkIndexer] {drive}: refresh cancelled, keeping the last checkpoint.");
            // A drive removed from config already had its status entry deleted (NetworkIndexer.Configure),
            // so SetStatus's own "only update an entry that still exists" guard makes this a no-op there --
            // this only actually reverts the status for a drive a user stopped via CancelDrive while it
            // remains configured, so it shows what's on disk from the last checkpoint instead of being
            // stuck on "indexing" forever.
            _setStatus(drive, "cached", null, null);
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkIndexer] Failed to index {drive}: {ex.Message}", LogLevel.Error);
            _setStatus(drive, "error", null, ex.Message);
        }
    }

    // Directories-listed ratio is a proxy for "how far the previous pass got": a directory only carries
    // FileRecordFlags.Listed once its own children were fully captured, so this is what TreeDiffBaseline
    // will actually be able to trust and skip re-listing, as opposed to just the raw record count.
    private static void LogResumeProgress(string drive, FileRecordStore? previousStore)
    {
        if (previousStore == null)
        {
            Logger.Log($"[NetworkIndexer] {drive}: no previous index to resume from, starting a fresh scan.");
            return;
        }

        var totalDirs = 0;
        var listedDirs = 0;
        foreach (var record in previousStore.Records)
        {
            if (!record.IsDirectory)
                continue;
            totalDirs++;
            if ((record.Flags & FileRecordFlags.Listed) != 0)
                listedDirs++;
        }

        Logger.Log($"[NetworkIndexer] {drive}: resuming with {previousStore.Records.Count} records from last pass " +
            $"({listedDirs}/{totalDirs} directories confirmed listed, previous IsComplete={previousStore.IsComplete}).");
    }
}
