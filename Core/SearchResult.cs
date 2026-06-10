namespace SwiftList.Core;

public class SearchResponse
{
    public List<SearchResult> FileResults { get; set; } = new();
    public List<SearchResult> AppResults { get; set; } = new();
}

public class SearchResult
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsDir { get; set; }
    public string Drive { get; set; } = string.Empty;
    internal ulong RankSortKey { get; set; }
}

public sealed class SearchResultRankComparer : IComparer<SearchResult>
{
    public static readonly SearchResultRankComparer Instance = new();

    private SearchResultRankComparer()
    {
    }

    public int Compare(SearchResult? left, SearchResult? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;

        var leftHistoryPriority = SearchHistoryStore.GetPriority(left.Path);
        var rightHistoryPriority = SearchHistoryStore.GetPriority(right.Path);
        var compare = leftHistoryPriority.CompareTo(rightHistoryPriority);
        if (compare != 0)
            return compare;

        compare = left.RankSortKey.CompareTo(right.RankSortKey);
        if (compare != 0)
            return compare;

        compare = left.Path.Length.CompareTo(right.Path.Length);
        if (compare != 0)
            return compare;

        compare = string.Compare(left.Drive, right.Drive, StringComparison.OrdinalIgnoreCase);
        if (compare != 0)
            return compare;

        return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
    }
}
