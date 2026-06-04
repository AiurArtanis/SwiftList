using System;

namespace SwiftList.Core.SearchIndex.Fzf
{
    internal readonly record struct FzfRank(int EntryIndex, int Score, ulong SortKey);

    internal static class FzfResultRank
    {
        public static FzfRank ForPathScheme(int entryIndex, string text, FzfPatternResult match)
        {
            ushort point0 = ScorePoint(match.Score);
            ushort point1 = PathnamePoint(text, match);
            ushort point2 = LengthPoint(text);
            return new FzfRank(entryIndex, match.Score, Pack(point1, point2, point0, 0));
        }

        public static FzfRank ForDefaultScheme(int entryIndex, string text, FzfPatternResult match)
        {
            ushort point0 = MatchPositionPoint(text, match);
            ushort point1 = MatchSpanPoint(match);
            ushort point2 = ScorePoint(match.Score);
            ushort point3 = LengthPoint(text);
            return new FzfRank(entryIndex, match.Score, Pack(point0, point1, point3, point2));
        }

        public static int Compare(FzfRank left, FzfRank right)
        {
            int compare = left.SortKey.CompareTo(right.SortKey);
            if (compare != 0)
                return compare;
            return left.EntryIndex.CompareTo(right.EntryIndex);
        }

        public static int CompareKeyOnly(FzfRank left, FzfRank right)
        {
            return left.SortKey.CompareTo(right.SortKey);
        }

        public static int CompareResults(SearchResult left, SearchResult right)
        {
            return SearchResultRankComparer.Instance.Compare(left, right);
        }

        private static ulong Pack(ushort point0, ushort point1, ushort point2, ushort point3)
        {
            return point0 |
                   ((ulong)point1 << 16) |
                   ((ulong)point2 << 32) |
                   ((ulong)point3 << 48);
        }

        private static ushort ScorePoint(int score)
        {
            return (ushort)(ushort.MaxValue - ClampToUShort(score));
        }

        private static ushort LengthPoint(string text)
        {
            return ClampToUShort(text.Trim().Length);
        }

        private static ushort MatchPositionPoint(string text, FzfPatternResult match)
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

        private static bool IsWordBoundary(string text, int index)
        {
            if (index <= 0 || index >= text.Length)
                return index == 0;

            char previous = text[index - 1];
            char current = text[index];
            if (!char.IsLetterOrDigit(previous))
                return true;

            return char.IsLower(previous) && (char.IsUpper(current) || char.IsDigit(current));
        }

        private static ushort PathnamePoint(string text, FzfPatternResult match)
        {
            if (!match.ValidOffsetFound)
                return ushort.MaxValue;

            int lastDelim = -1;
            for (int i = text.Length - 1; i >= 0; i--)
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
}
