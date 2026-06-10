using System;

namespace SwiftList.Core.SearchIndex.Fzf
{
    internal static class FzfExactMatcher
    {
        public static FzfMatchResult ExactMatch(string text, string pattern, bool caseSensitive, FzfScoringScheme scheme, bool boundaryCheck)
        {
            if (pattern.Length == 0 || pattern.Length > text.Length)
                return FzfMatchResult.NoMatch;

            int bestPos = -1;
            int bestBonus = -1;

            var textSpan = text.AsSpan();
            var patternSpan = pattern.AsSpan();
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            int offset = 0;
            while (offset <= text.Length - pattern.Length)
            {
                int index = textSpan.Slice(offset).IndexOf(patternSpan, comparison);
                if (index < 0)
                    break;

                int i = offset + index;
                int bonus = FzfAlgorithm.BonusAt(text, i, scheme);
                if (!boundaryCheck || IsBoundaryMatch(text, i, i + pattern.Length, bonus))
                {
                    if (bonus > bestBonus)
                    {
                        bestPos = i;
                        bestBonus = bonus;
                        if (bonus >= FzfAlgorithm.BonusBoundary)
                            break;
                    }
                }

                offset = i + 1;
            }

            if (bestPos < 0)
                return FzfMatchResult.NoMatch;

            int end = bestPos + pattern.Length;
            int score = boundaryCheck
                ? FzfAlgorithm.ScoreMatch * pattern.Length + FzfAlgorithm.BonusBoundaryWhite * (pattern.Length + 1) + bestBonus
                : FzfScoring.CalculateScore(text, pattern, bestPos, end, caseSensitive, scheme);
            return new FzfMatchResult(bestPos, end, score);
        }

        public static FzfMatchResult PrefixMatch(string text, string pattern, bool caseSensitive, FzfScoringScheme scheme)
        {
            if (pattern.Length == 0)
                return new FzfMatchResult(0, 0, 0);
            int start = char.IsWhiteSpace(pattern[0]) ? 0 : FzfAlgorithm.LeadingWhitespaces(text);
            if (text.Length - start < pattern.Length || !SpanEquals(text, start, pattern, caseSensitive))
                return FzfMatchResult.NoMatch;

            int end = start + pattern.Length;
            return new FzfMatchResult(start, end, FzfScoring.CalculateScore(text, pattern, start, end, caseSensitive, scheme));
        }

        public static FzfMatchResult SuffixMatch(string text, string pattern, bool caseSensitive, FzfScoringScheme scheme)
        {
            int trimmedLength = pattern.Length == 0 || !char.IsWhiteSpace(pattern[^1])
                ? text.Length - FzfAlgorithm.TrailingWhitespaces(text)
                : text.Length;
            if (pattern.Length == 0)
                return new FzfMatchResult(trimmedLength, trimmedLength, 0);

            int start = trimmedLength - pattern.Length;
            if (start < 0 || !SpanEquals(text, start, pattern, caseSensitive))
                return FzfMatchResult.NoMatch;

            return new FzfMatchResult(start, trimmedLength, FzfScoring.CalculateScore(text, pattern, start, trimmedLength, caseSensitive, scheme));
        }

        public static FzfMatchResult EqualMatch(string text, string pattern, bool caseSensitive, FzfScoringScheme scheme)
        {
            if (pattern.Length == 0)
                return FzfMatchResult.NoMatch;
            int start = char.IsWhiteSpace(pattern[0]) ? 0 : FzfAlgorithm.LeadingWhitespaces(text);
            int trailing = char.IsWhiteSpace(pattern[^1]) ? 0 : FzfAlgorithm.TrailingWhitespaces(text);
            if (text.Length - start - trailing != pattern.Length || !SpanEquals(text, start, pattern, caseSensitive))
                return FzfMatchResult.NoMatch;

            return new FzfMatchResult(
                start,
                start + pattern.Length,
                (FzfAlgorithm.ScoreMatch + FzfAlgorithm.BonusBoundaryWhite) * pattern.Length + (FzfAlgorithm.BonusFirstCharMultiplier - 1) * FzfAlgorithm.BonusBoundaryWhite);
        }

        private static bool SpanEquals(string text, int start, string pattern, bool caseSensitive)
        {
            return text.AsSpan(start, pattern.Length).Equals(pattern, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBoundaryMatch(string text, int start, int end, int startBonus)
        {
            if (startBonus < FzfAlgorithm.BonusBoundary)
                return false;
            if (start > 0 && FzfAlgorithm.GetClass(text[start - 1]) > FzfAlgorithm.CharClass.Delimiter)
                return false;
            return end >= text.Length || FzfAlgorithm.GetClass(text[end]) <= FzfAlgorithm.CharClass.Delimiter;
        }
    }
}
