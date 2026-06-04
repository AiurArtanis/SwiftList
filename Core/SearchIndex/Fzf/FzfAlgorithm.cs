using System;
using System.IO;

namespace SwiftList.Core.SearchIndex.Fzf
{
    internal enum FzfTermKind
    {
        Fuzzy,
        Exact,
        ExactBoundary,
        Prefix,
        Suffix,
        Equal
    }

    internal enum FzfScoringScheme
    {
        Default,
        Path,
        History
    }

    internal readonly record struct FzfMatchResult(int Start, int End, int Score)
    {
        public bool IsMatch => Start >= 0;
        public static FzfMatchResult NoMatch => new(-1, -1, 0);
    }

    internal static class FzfAlgorithm
    {
        public const int ScoreMatch = 16;
        public const int ScoreGapStart = -3;
        public const int ScoreGapExtension = -1;
        public const int BonusBoundary = ScoreMatch / 2;
        public const int BonusNonWord = ScoreMatch / 2;
        public const int BonusCamel123 = BonusBoundary + ScoreGapExtension;
        public const int BonusConsecutive = -(ScoreGapStart + ScoreGapExtension);
        public const int BonusFirstCharMultiplier = 2;
        public const int BonusBoundaryWhite = BonusBoundary + 2;
        public const int BonusBoundaryDelimiter = BonusBoundary + 1;
        public const int MaxV2Cells = 250_000;

        public static FzfMatchResult Match(
            FzfTermKind kind,
            string text,
            string pattern,
            bool caseSensitive,
            FzfScoringScheme scheme,
            FzfSlab? slab = null)
        {
            return kind switch
            {
                FzfTermKind.Fuzzy => FzfFuzzyMatcher.FuzzyMatchV2(text, pattern, caseSensitive, scheme, slab),
                FzfTermKind.Exact => FzfExactMatcher.ExactMatch(text, pattern, caseSensitive, scheme, boundaryCheck: false),
                FzfTermKind.ExactBoundary => FzfExactMatcher.ExactMatch(text, pattern, caseSensitive, scheme, boundaryCheck: true),
                FzfTermKind.Prefix => FzfExactMatcher.PrefixMatch(text, pattern, caseSensitive, scheme),
                FzfTermKind.Suffix => FzfExactMatcher.SuffixMatch(text, pattern, caseSensitive, scheme),
                FzfTermKind.Equal => FzfExactMatcher.EqualMatch(text, pattern, caseSensitive, scheme),
                _ => FzfMatchResult.NoMatch
            };
        }

        public static FzfMatchResult FuzzyMatchV1(string text, string pattern, bool caseSensitive, FzfScoringScheme scheme)
        {
            return FzfFuzzyMatcher.FuzzyMatchV1(text, pattern, caseSensitive, scheme);
        }

        public static bool CharsEqual(char text, char pattern, bool caseSensitive)
        {
            return caseSensitive
                ? text == pattern
                : char.ToLowerInvariant(text) == pattern;
        }

        public static char NormalizeChar(char c, bool caseSensitive)
        {
            return caseSensitive ? c : char.ToLowerInvariant(c);
        }

        public static int BonusAt(string text, int index, FzfScoringScheme scheme)
        {
            if (index == 0)
                return BonusFor(InitialClass(scheme), GetClass(text[index]), scheme);
            return BonusFor(GetClass(text[index - 1]), GetClass(text[index]), scheme);
        }

        public static int BonusFor(CharClass previous, CharClass current, FzfScoringScheme scheme)
        {
            if (current >= CharClass.NonWord)
            {
                if (previous == CharClass.White)
                    return BoundaryWhiteBonus(scheme);
                if (previous == CharClass.Delimiter)
                    return BoundaryDelimiterBonus(scheme);
                if (previous == CharClass.NonWord)
                    return BonusBoundary;
            }

            if ((previous == CharClass.Lower && current == CharClass.Upper) ||
                (previous != CharClass.Number && current == CharClass.Number))
                return BonusCamel123;

            return current switch
            {
                CharClass.NonWord or CharClass.Delimiter => BonusNonWord,
                CharClass.White => BoundaryWhiteBonus(scheme),
                _ => 0
            };
        }

        public static CharClass InitialClass(FzfScoringScheme scheme)
        {
            return scheme == FzfScoringScheme.Path ? CharClass.Delimiter : CharClass.White;
        }

        private static int BoundaryWhiteBonus(FzfScoringScheme scheme)
        {
            return scheme == FzfScoringScheme.Default ? BonusBoundaryWhite : BonusBoundary;
        }

        private static int BoundaryDelimiterBonus(FzfScoringScheme scheme)
        {
            return scheme == FzfScoringScheme.History ? BonusBoundary : BonusBoundaryDelimiter;
        }

        public static CharClass GetClass(char c)
        {
            if (c >= 'a' && c <= 'z')
                return CharClass.Lower;
            if (c >= 'A' && c <= 'Z')
                return CharClass.Upper;
            if (c >= '0' && c <= '9')
                return CharClass.Number;
            if (char.IsWhiteSpace(c))
                return CharClass.White;
            if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar || c == ':' || c == ';' || c == ',' || c == '|')
                return CharClass.Delimiter;
            if (char.IsLetter(c))
                return CharClass.Letter;
            return CharClass.NonWord;
        }

        public static ulong GetCharMask(ReadOnlySpan<char> span)
        {
            ulong mask = 0;
            for (int i = 0; i < span.Length; i++)
            {
                char c = span[i];
                char lower = char.ToLowerInvariant(c);
                int bit = lower switch
                {
                    >= 'a' and <= 'z' => lower - 'a',
                    >= '0' and <= '9' => 26 + (lower - '0'),
                    _ => 36 + (lower % 28)
                };
                mask |= (1UL << bit);
            }
            return mask;
        }

        public static ulong GetCharMask(string text)
        {
            return GetCharMask(text.AsSpan());
        }

        public static int LeadingWhitespaces(string text)
        {
            int i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            return i;
        }

        public static int TrailingWhitespaces(string text)
        {
            int count = 0;
            for (int i = text.Length - 1; i >= 0 && char.IsWhiteSpace(text[i]); i--)
                count++;
            return count;
        }

        public enum CharClass
        {
            White = 0,
            NonWord = 1,
            Delimiter = 2,
            Lower = 3,
            Upper = 4,
            Letter = 5,
            Number = 6
        }
    }
}
