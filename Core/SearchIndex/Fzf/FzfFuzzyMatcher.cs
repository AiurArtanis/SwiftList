using System;

namespace SwiftList.Core.SearchIndex.Fzf
{
    internal static class FzfFuzzyMatcher
    {
        public static FzfMatchResult FuzzyMatchV2(string text, string pattern, bool caseSensitive, FzfScoringScheme scheme, FzfSlab? slab = null)
        {
            int m = pattern.Length;
            if (m == 0)
                return new FzfMatchResult(0, 0, 0);
            int n = text.Length;
            if (m > n)
                return FzfMatchResult.NoMatch;
            if (!FzfScoring.FindFuzzyScope(text, pattern, caseSensitive, out int minIdx, out int maxIdx))
                return FzfMatchResult.NoMatch;

            int scopedLength = maxIdx - minIdx;
            if (m > 1000 || (long)scopedLength * m > FzfAlgorithm.MaxV2Cells)
                return FuzzyMatchV1(text, pattern, caseSensitive, scheme);

            var chars = slab?.Chars(scopedLength) ?? new char[scopedLength];
            var bonus = slab?.Bonus(scopedLength) ?? new short[scopedLength];
            var first = slab?.First(m) ?? new int[m];
            Array.Fill(first, -1, 0, m);

            int patternIndex = 0;
            int lastIdx = 0;
            char firstPatternChar = pattern[0];
            FzfAlgorithm.CharClass previousClass = FzfAlgorithm.InitialClass(scheme);
            for (int offset = 0; offset < scopedLength; offset++)
            {
                char raw = text[minIdx + offset];
                FzfAlgorithm.CharClass currentClass = FzfAlgorithm.GetClass(raw);
                char normalized = FzfAlgorithm.NormalizeChar(raw, caseSensitive);
                chars[offset] = normalized;
                bonus[offset] = (short)FzfAlgorithm.BonusFor(previousClass, currentClass, scheme);
                previousClass = currentClass;

                if (patternIndex < m && normalized == pattern[patternIndex])
                {
                    first[patternIndex] = offset;
                    lastIdx = offset;
                    patternIndex++;
                }
            }

            if (patternIndex != m)
                return FzfMatchResult.NoMatch;

            if (m == 1)
            {
                int bestScore = 0;
                int bestPos = -1;
                for (int i = 0; i < scopedLength; i++)
                {
                    if (chars[i] != firstPatternChar)
                        continue;
                    int score = FzfAlgorithm.ScoreMatch + bonus[i] * FzfAlgorithm.BonusFirstCharMultiplier;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPos = i;
                        if (bonus[i] >= FzfAlgorithm.BonusBoundary)
                            break;
                    }
                }

                return bestPos >= 0
                    ? new FzfMatchResult(minIdx + bestPos, minIdx + bestPos + 1, bestScore)
                    : FzfMatchResult.NoMatch;
            }

            int f0 = first[0];
            int width = lastIdx - f0 + 1;
            int matrixLength = m * width;
            var scores = slab?.Scores(matrixLength) ?? new short[matrixLength];
            var consecutive = slab?.Consecutive(matrixLength) ?? new short[matrixLength];

            bool inGap = false;
            short previous = 0;
            for (int col = f0; col <= lastIdx; col++)
            {
                int rel = col - f0;
                if (chars[col] == firstPatternChar)
                {
                    short score = (short)(FzfAlgorithm.ScoreMatch + bonus[col] * FzfAlgorithm.BonusFirstCharMultiplier);
                    scores[rel] = score;
                    consecutive[rel] = 1;
                    previous = score;
                    inGap = false;
                }
                else
                {
                    short score = (short)Math.Max(previous + (inGap ? FzfAlgorithm.ScoreGapExtension : FzfAlgorithm.ScoreGapStart), 0);
                    scores[rel] = score;
                    consecutive[rel] = 0;
                    previous = score;
                    inGap = true;
                }
            }

            int maxScore = 0;
            int maxScorePos = f0;
            for (int pidx = 1; pidx < m; pidx++)
            {
                int row = pidx * width;
                int previousRow = row - width;
                inGap = false;
                int start = first[pidx];
                int startRel = start - f0;
                if (startRel > 0)
                {
                    scores[row + startRel - 1] = 0;
                    consecutive[row + startRel - 1] = 0;
                }
                for (int col = start; col <= lastIdx; col++)
                {
                    int rel = col - f0;
                    short s2 = rel > 0
                        ? (short)(scores[row + rel - 1] + (inGap ? FzfAlgorithm.ScoreGapExtension : FzfAlgorithm.ScoreGapStart))
                        : (short)0;

                    short s1 = 0;
                    short consecutiveScore = 0;
                    if (chars[col] == pattern[pidx] && rel > 0)
                    {
                        s1 = (short)(scores[previousRow + rel - 1] + FzfAlgorithm.ScoreMatch);
                        short b = bonus[col];
                        consecutiveScore = (short)(consecutive[previousRow + rel - 1] + 1);
                        if (consecutiveScore > 1)
                        {
                            short firstBonus = bonus[col - consecutiveScore + 1];
                            if (b >= FzfAlgorithm.BonusBoundary && b > firstBonus)
                            {
                                consecutiveScore = 1;
                            }
                            else
                            {
                                b = (short)Math.Max(Math.Max((int)b, firstBonus), FzfAlgorithm.BonusConsecutive);
                            }
                        }

                        if (s1 + b < s2)
                        {
                            s1 += bonus[col];
                            consecutiveScore = 0;
                        }
                        else
                        {
                            s1 += b;
                        }
                    }

                    consecutive[row + rel] = consecutiveScore;
                    inGap = s1 < s2;
                    short cellScore = (short)Math.Max(Math.Max((int)s1, s2), 0);
                    scores[row + rel] = cellScore;

                    if (pidx == m - 1 && cellScore > maxScore)
                    {
                        maxScore = cellScore;
                        maxScorePos = col;
                    }
                }
            }

            int startIndex = BacktrackStart(scores, consecutive, first, f0, width, m, maxScorePos);
            return new FzfMatchResult(minIdx + startIndex, minIdx + maxScorePos + 1, maxScore);
        }

        public static FzfMatchResult FuzzyMatchV1(string text, string pattern, bool caseSensitive, FzfScoringScheme scheme)
        {
            if (pattern.Length == 0)
                return new FzfMatchResult(0, 0, 0);
            if (!FzfScoring.FindFuzzyScope(text, pattern, caseSensitive, out int start, out int end))
                return FzfMatchResult.NoMatch;

            int patternIndex = pattern.Length - 1;
            int shrinkStart = start;
            for (int i = end - 1; i >= start; i--)
            {
                if (FzfAlgorithm.CharsEqual(text[i], pattern[patternIndex], caseSensitive))
                {
                    patternIndex--;
                    if (patternIndex < 0)
                    {
                        shrinkStart = i;
                        break;
                    }
                }
            }

            int score = FzfScoring.CalculateScore(text, pattern, shrinkStart, end, caseSensitive, scheme);
            return new FzfMatchResult(shrinkStart, end, score);
        }

        private static int BacktrackStart(short[] scores, short[] consecutive, int[] first, int f0, int width, int patternLength, int maxScorePos)
        {
            int i = patternLength - 1;
            int j = maxScorePos;
            bool preferMatch = true;
            while (i >= 0 && j >= first[i])
            {
                int row = i * width;
                int rel = j - f0;
                short score = scores[row + rel];
                short diagonal = i > 0 && rel > 0 ? scores[row - width + rel - 1] : (short)0;
                short left = rel > 0 ? scores[row + rel - 1] : (short)0;

                if (score > diagonal && (score > left || score == left && preferMatch))
                {
                    if (i == 0)
                        return j;
                    i--;
                }

                preferMatch = consecutive[row + rel] > 1 ||
                              (row + width + rel + 1 < consecutive.Length && consecutive[row + width + rel + 1] > 0);
                j--;
            }

            return Math.Max(0, first[0]);
        }
    }
}
