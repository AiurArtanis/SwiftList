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

        Parallel.ForEach(
            snapshots,
            new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = 2
            },
            index =>
            {
                token.ThrowIfCancellationRequested();
                if (!IsDriveAllowed(index.Drive, parsed, directoryFilterLower))
                    return;

                index.SearchStreaming(parsed, query, directoryFilterLower, limit, onResult, token);
            });
    }

    private static bool IsDriveAllowed(string indexDrive, ParsedSearchQuery parsed, string? directoryFilterLower)
    {
        // The "d:foo" query-scoping modifier only makes sense against a bare drive letter -- a
        // folder-index or UNC key (anything longer) can never equal it, so it's always excluded once
        // a target drive is requested.
        if (parsed.TargetDrive != null && !(indexDrive.Length == 1 && parsed.TargetDrive.Equals(indexDrive, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (directoryFilterLower == null)
            return true;

        if (indexDrive.Length == 1)
            return directoryFilterLower.StartsWith(indexDrive + @":\", StringComparison.OrdinalIgnoreCase);

        // A folder-index or UNC key is already a full rooted path -- allow either direction: the
        // filter scope nested under the index root, or the index root nested under a broader filter
        // scope (e.g. a folder index for D:\Projects\ProjectA should still be searched when the
        // filter scope is the parent D:\Projects\). Compare with a trailing separator on both sides
        // so "D:\Foo" doesn't also match a filter under a sibling "D:\FooBar".
        var rootedDrive = indexDrive.EndsWith(Path.DirectorySeparatorChar) ? indexDrive : indexDrive + Path.DirectorySeparatorChar;
        var rootedFilter = directoryFilterLower.EndsWith(Path.DirectorySeparatorChar) ? directoryFilterLower : directoryFilterLower + Path.DirectorySeparatorChar;
        return rootedFilter.StartsWith(rootedDrive, StringComparison.OrdinalIgnoreCase) || rootedDrive.StartsWith(rootedFilter, StringComparison.OrdinalIgnoreCase);
    }
}
