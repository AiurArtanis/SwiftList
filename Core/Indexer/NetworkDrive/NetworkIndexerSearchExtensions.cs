namespace SwiftList.Core.Indexer.NetworkDrive;

public static class NetworkIndexerSearchExtensions
{

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
