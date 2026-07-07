using SwiftList.Core.Indexer.Usn;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core;

// Backs the GetRecentFiles pipe request: for each configured target directory that resolves to a local
// drive letter, walks its subtree in the in-memory index (no disk I/O) via RecentFilesWalker and returns
// the most recently created entries across all of them. Network/WSL directories are handled separately
// by NetworkIndexerRecentFilesExtensions -- this indexer only tracks local drive letters.
public static class SearchEngineRecentFilesExtensions
{
    public static List<SearchResult> GetRecentFiles(this UsnIndexer indexer, IReadOnlyList<string> directories, int limit, int maxAgeMinutes)
    {
        // maxAgeMinutes <= 0 (an unset/invalid value slipping through) means "no age cutoff" rather
        // than "cutoff at now", which would silently return nothing.
        var cutoffUtc = maxAgeMinutes > 0
            ? (uint)Math.Max(0, DateTimeOffset.UtcNow.AddMinutes(-maxAgeMinutes).ToUnixTimeSeconds())
            : 0u;

        var candidates = new List<SearchResult>();
        lock (indexer.LockObj)
        {
            foreach (var dir in directories)
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;

                var root = Path.GetPathRoot(dir);
                if (string.IsNullOrEmpty(root) || !char.IsLetter(root[0]))
                    continue;

                var drive = root[0].ToString().ToUpperInvariant();
                if (!indexer._recordIndexes.TryGetValue(drive, out var index))
                    continue;

                RecentFilesWalker.CollectFromDirectory(index, dir.ToLowerInvariant(), drive, cutoffUtc, candidates);
            }
        }

        // limit <= 0 means "unlimited" -- still bounded by RecentFilesWalker.MaxScannedPerDirectory and
        // the age cutoff above, so this can't turn into an unbounded full-volume dump.
        var ordered = candidates.OrderByDescending(c => c.CreatedUtc);
        return (limit > 0 ? ordered.Take(limit) : ordered).ToList();
    }
}
