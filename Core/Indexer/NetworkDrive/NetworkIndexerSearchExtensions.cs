using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.Indexer.NetworkDrive;

public static class NetworkIndexerSearchExtensions
{
    public static List<SearchResult> Search(
        this NetworkIndexer indexer,
        string query,
        int limit,
        CancellationToken token = default,
        string? directoryFilter = null)
    {
        indexer.EnsureConfigured();
        if (limit <= 0 || string.IsNullOrWhiteSpace(query))
            return new List<SearchResult>();

        NetworkIndex[] snapshots;
        lock (indexer.Gate)
            snapshots = indexer._indexes.Values.ToArray();

        if (snapshots.Length == 0)
            return new List<SearchResult>();

        var parsed = SearchQueryParser.Parse(query);
        var directoryFilterLower = IndexerHelper.NormalizeFilter(directoryFilter);
        var results = new List<SearchResult>(Math.Min(limit, 64));

        var resultsLock = new object();
        Parallel.ForEach(snapshots, new ParallelOptions { CancellationToken = token }, index =>
        {
            token.ThrowIfCancellationRequested();
            if (!IsDriveAllowed(index.Drive, parsed, directoryFilterLower))
                return;

            var localResults = new List<SearchResult>();
            index.Search(parsed, query, directoryFilterLower, limit, localResults, token);
            lock (resultsLock)
            {
                results.AddRange(localResults);
            }
        });

        results.Sort(FzfResultRank.CompareResults);
        if (results.Count > limit)
            results.RemoveRange(limit, results.Count - limit);

        return results;
    }

    public static void SearchStreaming(
        this NetworkIndexer indexer,
        string query,
        int limit,
        Action<SearchResult> onResult,
        CancellationToken token = default,
        string? directoryFilter = null)
    {
        indexer.EnsureConfigured();
        if (limit <= 0 || string.IsNullOrWhiteSpace(query))
            return;

        NetworkIndex[] snapshots;
        lock (indexer.Gate)
            snapshots = indexer._indexes.Values.ToArray();

        if (snapshots.Length == 0)
            return;

        var parsed = SearchQueryParser.Parse(query);
        var directoryFilterLower = IndexerHelper.NormalizeFilter(directoryFilter);

        Parallel.ForEach(snapshots, new ParallelOptions { CancellationToken = token }, index =>
        {
            token.ThrowIfCancellationRequested();
            if (!IsDriveAllowed(index.Drive, parsed, directoryFilterLower))
                return;

            index.SearchStreaming(parsed, query, directoryFilterLower, limit, onResult, token);
        });
    }

    private static bool IsDriveAllowed(string indexDrive, ParsedSearchQuery parsed, string? directoryFilterLower)
    {
        if (parsed.TargetDrive != null && !parsed.TargetDrive.Equals(indexDrive, StringComparison.OrdinalIgnoreCase))
            return false;

        if (directoryFilterLower == null)
            return true;

        return directoryFilterLower.StartsWith(indexDrive + @":\", StringComparison.OrdinalIgnoreCase);
    }
}
