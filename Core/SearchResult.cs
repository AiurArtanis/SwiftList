namespace SwiftList.Core;


public class SearchResult
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsDir { get; set; }
    public string Drive { get; set; } = string.Empty;
    public FileAttributes Attributes { get; set; }
    internal ulong RankSortKey { get; set; }

    // Only populated (and only meaningful) for GetRecentFiles results -- lets SearchService merge the
    // local-drive and network/WSL result sets by actual recency instead of just concatenating two
    // already-sorted-but-incomparable lists. Zero for every other kind of search result. Last-write time,
    // not creation time: a long-lived file you just edited should still count as "recent".
    public uint ModifiedUtc { get; set; }
}

public sealed class SearchResultRankComparer : IComparer<SearchResult>
{
    public static readonly SearchResultRankComparer Instance = new(new Dictionary<string, int>());

    private readonly IReadOnlyDictionary<string, int> _historySnapshot;

    public SearchResultRankComparer(IReadOnlyDictionary<string, int> historySnapshot) => _historySnapshot = historySnapshot;

    private static string NormalizeForLookup(string path)
    {
        if (path.Length > 3 && path[^1] == '\\')
            return path.TrimEnd('\\');
        return path;
    }

    public int Compare(SearchResult? left, SearchResult? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;

        var leftPath = NormalizeForLookup(left.Path);
        var rightPath = NormalizeForLookup(right.Path);

        var leftHistoryPriority = _historySnapshot.TryGetValue(leftPath, out var lp) ? lp : int.MaxValue;
        var rightHistoryPriority = _historySnapshot.TryGetValue(rightPath, out var rp) ? rp : int.MaxValue;

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
