using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.SearchIndex.RecordSearch;

public sealed class Searcher
{
    private readonly CacheManager _cacheManager = new();

    internal CacheManager CacheManager => _cacheManager;

    public List<SearchResult> Search(RuntimeIndex index, string query, int limit, CancellationToken token, string? directoryFilter = null)
    {
        if (limit <= 0 || string.IsNullOrWhiteSpace(query))
            return new List<SearchResult>();

        var parsed = SearchQueryParser.Parse(query);
        var directoryFilterLower = Helpers.NormalizeFilter(directoryFilter);

        if (parsed.IsPathMode)
        {
            try
            {
                return this.SearchPath(index, parsed, limit, token, directoryFilterLower);
            }
            finally
            {
                index.ClearPathCache();
                Helpers.RequestBackgroundCompaction();
            }
        }

        var pattern = Helpers.GetPattern("q|" + query, query, parseText: false);
        if (pattern.IsEmpty && pattern.TargetDrive == null)
            return new List<SearchResult>();

        if (pattern.TargetDrive != null && !pattern.TargetDrive.Equals(index.SourceKey, StringComparison.OrdinalIgnoreCase))
            return new List<SearchResult>();

        try
        {
            if (index.DirectoryFilterExcludesSource(directoryFilterLower))
                return new List<SearchResult>();

            var directoryRootId = index.TryGetDirectoryRootId(directoryFilterLower);
            return this.SearchNames(index, pattern, limit, token, directoryFilterLower, directoryRootId);
        }
        finally
        {
            if (directoryFilterLower != null)
            {
                index.ClearPathCache();
                Helpers.RequestBackgroundCompaction();
            }
        }
    }

    public void SearchStreaming(RuntimeIndex index, string query, int limit, Action<SearchResult> onResult, CancellationToken token, string? directoryFilter = null)
    {
        if (limit <= 0 || string.IsNullOrWhiteSpace(query))
            return;

        var parsed = SearchQueryParser.Parse(query);
        var directoryFilterLower = Helpers.NormalizeFilter(directoryFilter);

        if (parsed.IsPathMode)
        {
            try
            {
                var results = this.SearchPath(index, parsed, limit, token, directoryFilterLower);
                foreach (var r in results)
                {
                    token.ThrowIfCancellationRequested();
                    onResult(r);
                }
                return;
            }
            finally
            {
                index.ClearPathCache();
                Helpers.RequestBackgroundCompaction();
            }
        }

        var pattern = Helpers.GetPattern("q|" + query, query, parseText: false);
        if (pattern.IsEmpty && pattern.TargetDrive == null)
            return;

        if (pattern.TargetDrive != null && !pattern.TargetDrive.Equals(index.SourceKey, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (index.DirectoryFilterExcludesSource(directoryFilterLower))
                return;

            var directoryRootId = index.TryGetDirectoryRootId(directoryFilterLower);
            this.SearchNamesStreaming(index, pattern, limit, onResult, token, directoryFilterLower, directoryRootId);
        }
        finally
        {
            if (directoryFilterLower != null)
            {
                index.ClearPathCache();
                Helpers.RequestBackgroundCompaction();
            }
        }
    }

    public void ClearCaches()
    {
        _cacheManager.Clear();
        Helpers.PatternCache.Clear();
    }
}
