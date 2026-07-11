namespace SwiftList.Core.SearchIndex.Fzf;

internal readonly record struct FzfRank(int EntryIndex, int Score, ulong SortKey);

internal static class FzfResultRank
{
    public static FzfRank ForPathScheme(int entryIndex, ReadOnlySpan<char> text, FzfPatternResult match)
    {
        var point0 = ScorePoint(match.Score);
        var point1 = PathnamePoint(text, match);
        var point2 = LengthPoint(text);
        return new FzfRank(entryIndex, match.Score, Pack(point1, point2, point0, 0));
    }

    public static FzfRank ForDefaultScheme(int entryIndex, ReadOnlySpan<char> text, FzfPatternResult match)
    {
        var point0 = MatchPositionPoint(text, match);
        var point1 = MatchSpanPoint(match);
        var point2 = ScorePoint(match.Score);
        var point3 = LengthPoint(text);
        return new FzfRank(entryIndex, match.Score, Pack(point0, point1, point3, point2));
    }

    // The name-dependent low 32 bits of the Default-scheme sort key (match-position point | span
    // point << 16) -- path mode reuses these per-unique and rebuilds the upper 32 bits per row.
    public static uint RankLow32(ReadOnlySpan<char> text, FzfPatternResult match)
        => MatchPositionPoint(text, match) | ((uint)MatchSpanPoint(match) << 16);

    public static int Compare(FzfRank left, FzfRank right)
    {
        var compare = left.SortKey.CompareTo(right.SortKey);
        if (compare != 0)
            return compare;
        return left.EntryIndex.CompareTo(right.EntryIndex);
    }

    public static int CompareKeyOnly(FzfRank left, FzfRank right) => left.SortKey.CompareTo(right.SortKey);

    public static int CompareResults(SearchResult left, SearchResult right) => SearchResultRankComparer.Instance.Compare(left, right);

    private static ulong Pack(ushort point0, ushort point1, ushort point2, ushort point3) => point0 |
               ((ulong)point1 << 16) |
               ((ulong)point2 << 32) |
               ((ulong)point3 << 48);

    private static ushort ScorePoint(int score) => (ushort)(ushort.MaxValue - ClampToUShort(score));

    private static ushort LengthPoint(ReadOnlySpan<char> text) => ClampToUShort(text.Trim().Length);

    private static ushort MatchPositionPoint(ReadOnlySpan<char> text, FzfPatternResult match)
    {
        if (!match.ValidOffsetFound)
            return ushort.MaxValue;

        if (match.MinBegin <= 0)
            return 0;

        return ClampToUShort((IsWordBoundary(text, match.MinBegin) ? 256 : 4096) + match.MinBegin);
    }

    private static ushort MatchSpanPoint(FzfPatternResult match)
    {
        if (!match.ValidOffsetFound)
            return ushort.MaxValue;

        return ClampToUShort(Math.Max(0, match.MaxEnd - match.MinBegin));
    }

    private static bool IsWordBoundary(ReadOnlySpan<char> text, int index)
    {
        if (index <= 0 || index >= text.Length)
            return index == 0;

        var previous = text[index - 1];
        var current = text[index];
        if (!char.IsLetterOrDigit(previous))
            return true;

        return char.IsLower(previous) && (char.IsUpper(current) || char.IsDigit(current));
    }

    private static ushort PathnamePoint(ReadOnlySpan<char> text, FzfPatternResult match)
    {
        if (!match.ValidOffsetFound)
            return ushort.MaxValue;

        var lastDelim = -1;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == '/' || text[i] == '\\')
            {
                lastDelim = i;
                break;
            }
        }

        return lastDelim <= match.MinBegin
            ? ClampToUShort(match.MinBegin - lastDelim)
            : ushort.MaxValue;
    }

    private static ushort ClampToUShort(int value)
    {
        if (value <= 0)
            return 0;
        return value >= ushort.MaxValue ? ushort.MaxValue : (ushort)value;
    }
}
