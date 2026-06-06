using System;
using System.Collections.Generic;
using SwiftList.Core;

namespace SwiftList.App.Converters
{
    internal static class FuzzyHighlightMatcher
    {
        public static void MarkFuzzyMatch(string text, string term, bool[] highlights)
        {
            if (string.IsNullOrEmpty(term))
                return;

            // Cache pinyin segments for text characters (each character can have multiple segments/pronunciations)
            string[][] segments = new string[text.Length][];
            for (int i = 0; i < text.Length; i++)
            {
                segments[i] = GetPinyinSegments(text[i]);
            }

            int[,] memo = new int[text.Length + 1, term.Length + 1];

            for (int i = 0; i <= text.Length; i++)
                for (int j = 0; j <= term.Length; j++)
                    memo[i, j] = -1;

            int maxScore = ComputeMaxScore(text, 0, term, 0, segments, memo);

            if (maxScore > 0)
            {
                int textIdx = 0;
                int termIdx = 0;
                while (textIdx < text.Length && termIdx < term.Length)
                {
                    int currentScore = memo[textIdx, termIdx];
                    if (currentScore == -1)
                        break;

                    bool choiceMade = false;

                    // Choice 1: Match any prefix of pinyin segment
                    foreach (string seg in segments[textIdx])
                    {
                        int maxLen = Math.Min(seg.Length, term.Length - termIdx);
                        int commonLen = 0;
                        while (commonLen < maxLen && seg[commonLen] == term[termIdx + commonLen])
                        {
                            commonLen++;
                        }

                        for (int l = commonLen; l >= 1; l--)
                        {
                            int nextScore = ComputeMaxScore(text, textIdx + 1, term, termIdx + l, segments, memo);
                            int bonus = GetMatchBonus(text, textIdx);
                            if (nextScore + bonus == currentScore)
                            {
                                highlights[textIdx] = true;
                                termIdx += l;
                                textIdx++;
                                choiceMade = true;
                                break;
                            }
                        }
                        if (choiceMade)
                            break;
                    }

                    // Choice 3: Match literal character
                    if (!choiceMade && text[textIdx] == term[termIdx])
                    {
                        int nextScore = ComputeMaxScore(text, textIdx + 1, term, termIdx + 1, segments, memo);
                        int bonus = GetMatchBonus(text, textIdx);
                        if (nextScore + bonus == currentScore)
                        {
                            highlights[textIdx] = true;
                            termIdx++;
                            textIdx++;
                            choiceMade = true;
                        }
                    }

                    // Choice 0: Skip text[textIdx]
                    if (!choiceMade)
                    {
                        int skipScore = ComputeMaxScore(text, textIdx + 1, term, termIdx, segments, memo);
                        if (skipScore == currentScore)
                        {
                            textIdx++;
                        }
                        else
                        {
                            textIdx++;
                        }
                    }
                }
            }
        }

        private static int ComputeMaxScore(
            string text,
            int textIdx,
            string term,
            int termIdx,
            string[][] segments,
            int[,] memo)
        {
            if (termIdx == term.Length)
                return 0;

            if (textIdx == text.Length)
                return -100000;

            if (memo[textIdx, termIdx] != -1)
                return memo[textIdx, termIdx];

            // Choice 0: Skip text[textIdx]
            int bestScore = ComputeMaxScore(text, textIdx + 1, term, termIdx, segments, memo);

            char tc = text[textIdx];
            int matchBonus = GetMatchBonus(text, textIdx);

            // Choice 1: Match prefix of pinyin segment
            foreach (string seg in segments[textIdx])
            {
                int maxLen = Math.Min(seg.Length, term.Length - termIdx);
                int commonLen = 0;
                while (commonLen < maxLen && seg[commonLen] == term[termIdx + commonLen])
                {
                    commonLen++;
                }

                for (int l = 1; l <= commonLen; l++)
                {
                    int score = ComputeMaxScore(text, textIdx + 1, term, termIdx + l, segments, memo) + matchBonus;
                    if (score >= bestScore)
                    {
                        bestScore = score;
                    }
                }
            }

            // Choice 3: Match literal character
            if (tc == term[termIdx])
            {
                int score = ComputeMaxScore(text, textIdx + 1, term, termIdx + 1, segments, memo) + matchBonus;
                if (score >= bestScore)
                {
                    bestScore = score;
                }
            }

            memo[textIdx, termIdx] = bestScore;
            return bestScore;
        }

        private static int GetMatchBonus(string text, int textIdx)
        {
            int lastDotIdx = text.LastIndexOf('.');
            int matchBonus = 10;
            if (lastDotIdx >= 0 && textIdx > lastDotIdx)
            {
                matchBonus = 1; // Heavy penalty for matching in the extension!
            }
            else if (textIdx == 0)
            {
                matchBonus += 15;
            }
            else if (IsDelimiter(text[textIdx - 1]))
            {
                matchBonus += 15;
            }
            return matchBonus;
        }

        private static bool IsDelimiter(char c)
        {
            return c == '.' || c == '_' || c == '-' || c == ' ' || c == '/' || c == '\\' ||
                   c == '(' || c == ')' || c == '[' || c == ']' || c == '|' || c == '│' || c == '\t';
        }

        private static string[] GetPinyinSegments(char c)
        {
            if (c <= 127)
                return new[] { c.ToString().ToLowerInvariant() };

            string s = c.ToString();
            var list = new List<string>();
            foreach (var provider in AliasProviderRegistry.GetActiveProviders())
            {
                try
                {
                    if (provider.CanHandle(s))
                    {
                        foreach (string alias in provider.GetAliases(s))
                        {
                            if (!string.IsNullOrWhiteSpace(alias))
                                list.Add(alias.ToLowerInvariant());
                        }
                    }
                }
                catch
                {
                    // Ignore
                }
            }
            if (list.Count == 0)
                list.Add(s.ToLowerInvariant());
            return list.ToArray();
        }
    }
}
