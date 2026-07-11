using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.IndexV2;

// Top-level search entry point for a single drive's LiveIndex, mirroring Searcher.SearchStreaming's
// dispatch: parse the query, route path-mode queries to PathSearch and everything else to NameSearch,
// normalize/gate the directory filter the same way. Runs entirely inside one LiveIndex.Read call so
// the whole search sees one consistent (Snapshot, DeltaOverlay) pair.
public static class IndexV2Searcher
{
    public static void SearchStreaming(LiveIndex index, string query, int limit, Action<SearchResult> onResult, CancellationToken token, string? directoryFilter = null)
    {
        if (limit <= 0 || string.IsNullOrWhiteSpace(query))
            return;

        var parsed = SearchQueryParser.Parse(query);
        index.Read<object?>((snapshot, delta) =>
        {
            var directoryFilterLower = DirectoryFilterResolver.NormalizeFilter(directoryFilter);
            if (directoryFilterLower != null && directoryFilterLower.Equals(snapshot.SourceRoot.ToLowerInvariant(), StringComparison.Ordinal))
                directoryFilterLower = null;

            if (parsed.IsPathMode)
            {
                PathSearch.SearchStreaming(snapshot, delta, parsed, limit, onResult, token, directoryFilterLower);
                return null;
            }

            var pattern = FzfPattern.Parse(query);
            NameSearch.SearchStreaming(snapshot, delta, pattern, limit, onResult, token, directoryFilterLower);
            return null;
        });
    }

    public static void GetRecentFiles(LiveIndex index, string dirLower, uint cutoffUtc, List<SearchResult> candidates)
    {
        index.Read<object?>((snapshot, delta) =>
        {
            RecentFilesV2.CollectFromDirectory(snapshot, delta, dirLower, snapshot.SourceKey, cutoffUtc, candidates);
            return null;
        });
    }
}
