using System;

namespace SwiftList.Core.SearchIndex.Fzf
{
    internal static class FzfScoring
    {
        public static bool FindFuzzyScope(string text, string pattern, bool caseSensitive, out int start, out int end)
        {
            start = -1;
            end = -1;
            
            var textSpan = text.AsSpan();
            int currentIdx = 0;
            char lastChar = '\0';
            
            for (int patternIndex = 0; patternIndex < pattern.Length; patternIndex++)
            {
                char target = pattern[patternIndex];
                int offset;
                if (caseSensitive)
                {
                    offset = textSpan.Slice(currentIdx).IndexOf(target);
                }
                else
                {
                    char lower = char.ToLowerInvariant(target);
                    char upper = char.ToUpperInvariant(target);
                    offset = lower == upper 
                        ? textSpan.Slice(currentIdx).IndexOf(lower)
                        : textSpan.Slice(currentIdx).IndexOfAny(lower, upper);
                }
                
                if (offset < 0)
                    return false;
                
                int absoluteIdx = currentIdx + offset;
                if (patternIndex == 0)
                    start = Math.Max(0, absoluteIdx - 1);
                
                lastChar = target;
                currentIdx = absoluteIdx + 1;
            }
            
            end = currentIdx;
            
            char l = char.ToLowerInvariant(lastChar);
            char u = char.ToUpperInvariant(lastChar);
            int lastOffset = caseSensitive ? textSpan.Slice(end).LastIndexOf(lastChar)
                : (l == u ? textSpan.Slice(end).LastIndexOf(l) : textSpan.Slice(end).LastIndexOfAny(l, u));
            
            if (lastOffset >= 0)
                end = end + lastOffset + 1;
                
            return true;
        }

        public static int CalculateScore(string text, string pattern, int start, int end, bool caseSensitive, FzfScoringScheme scheme)
        {
            int patternIndex = 0;
            int score = 0;
            bool inGap = false;
            int consecutive = 0;
            int firstBonus = 0;
            FzfAlgorithm.CharClass previousClass = start > 0 ? FzfAlgorithm.GetClass(text[start - 1]) : FzfAlgorithm.InitialClass(scheme);

            for (int i = start; i < end; i++)
            {
                FzfAlgorithm.CharClass currentClass = FzfAlgorithm.GetClass(text[i]);
                bool matched = patternIndex < pattern.Length && FzfAlgorithm.CharsEqual(text[i], pattern[patternIndex], caseSensitive);
                if (matched)
                {
                    int bonus = FzfAlgorithm.BonusFor(previousClass, currentClass, scheme);
                    score += FzfAlgorithm.ScoreMatch;
                    if (consecutive == 0)
                    {
                        firstBonus = bonus;
                    }
                    else
                    {
                        if (bonus >= FzfAlgorithm.BonusBoundary && bonus > firstBonus)
                            firstBonus = bonus;
                        bonus = Math.Max(Math.Max(bonus, firstBonus), FzfAlgorithm.BonusConsecutive);
                    }

                    score += patternIndex == 0 ? bonus * FzfAlgorithm.BonusFirstCharMultiplier : bonus;
                    patternIndex++;
                    consecutive++;
                    inGap = false;
                }
                else
                {
                    score += inGap ? FzfAlgorithm.ScoreGapExtension : FzfAlgorithm.ScoreGapStart;
                    inGap = true;
                    consecutive = 0;
                    firstBonus = 0;
                }

                previousClass = currentClass;
            }

            return patternIndex == pattern.Length ? score : -1;
        }
    }
}
