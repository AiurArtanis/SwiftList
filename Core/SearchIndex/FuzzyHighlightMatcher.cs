namespace SwiftList.Core.SearchIndex;

// Moved here (was App/Converters) so Core's ranking pipeline can compute the same match mask it
// scores against, not just App's display highlighting -- see HighlightMask, which is the shared
// entry point both now go through. Public since App still calls this directly for its own
// in-memory searchable lists (favorites, inline list items) that don't go through HighlightMask's
// FzfPattern-based term splitting.
public static class FuzzyHighlightMatcher
{
    public static void MarkFuzzyMatch(string text, string term, Span<bool> highlights, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(text))
            return;

        token.ThrowIfCancellationRequested();

        // Cache pinyin segments for text characters (each character can have multiple segments/pronunciations)
        var segments = new string[text.Length][];
        for (var i = 0; i < text.Length; i++)
        {
            segments[i] = GetPinyinSegments(text[i]);
        }

        var memo = new int[text.Length + 1, term.Length + 1];

        for (var i = 0; i <= text.Length; i++)
            for (var j = 0; j <= term.Length; j++)
                memo[i, j] = -1;

        var maxScore = ComputeMaxScore(text, 0, term, 0, segments, memo, token);

        if (maxScore > 0)
        {
            var textIdx = 0;
            var termIdx = 0;
            while (textIdx < text.Length && termIdx < term.Length)
            {
                token.ThrowIfCancellationRequested();
                var currentScore = memo[textIdx, termIdx];
                if (currentScore == -1)
                    break;

                var choiceMade = false;

                // Choice 1: Match any prefix of pinyin segment
                foreach (var seg in segments[textIdx])
                {
                    var maxLen = Math.Min(seg.Length, term.Length - termIdx);
                    var commonLen = 0;
                    while (commonLen < maxLen && seg[commonLen] == term[termIdx + commonLen])
                    {
                        commonLen++;
                    }

                    for (var l = commonLen; l >= 1; l--)
                    {
                        var nextScore = ComputeMaxScore(text, textIdx + 1, term, termIdx + l, segments, memo, token);
                        var bonus = GetMatchBonus(text, textIdx);
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
                    var nextScore = ComputeMaxScore(text, textIdx + 1, term, termIdx + 1, segments, memo, token);
                    var bonus = GetMatchBonus(text, textIdx);
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
                    var skipScore = ComputeMaxScore(text, textIdx + 1, term, termIdx, segments, memo, token);
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
        int[,] memo,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (termIdx == term.Length)
            return 0;

        if (textIdx == text.Length)
            return -100000;

        if (memo[textIdx, termIdx] != -1)
            return memo[textIdx, termIdx];

        // Choice 0: Skip text[textIdx]
        var bestScore = ComputeMaxScore(text, textIdx + 1, term, termIdx, segments, memo, token);

        var tc = text[textIdx];
        var matchBonus = GetMatchBonus(text, textIdx);

        // Choice 1: Match prefix of pinyin segment
        foreach (var seg in segments[textIdx])
        {
            var maxLen = Math.Min(seg.Length, term.Length - termIdx);
            var commonLen = 0;
            while (commonLen < maxLen && seg[commonLen] == term[termIdx + commonLen])
            {
                commonLen++;
            }

            for (var l = 1; l <= commonLen; l++)
            {
                var score = ComputeMaxScore(text, textIdx + 1, term, termIdx + l, segments, memo, token) + matchBonus;
                if (score >= bestScore)
                {
                    bestScore = score;
                }
            }
        }

        // Choice 3: Match literal character
        if (tc == term[termIdx])
        {
            var score = ComputeMaxScore(text, textIdx + 1, term, termIdx + 1, segments, memo, token) + matchBonus;
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
        var lastDotIdx = text.LastIndexOf('.');
        var matchBonus = 10;
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

    private static bool IsDelimiter(char c) => c == '.' || c == '_' || c == '-' || c == ' ' || c == '/' || c == '\\' ||
               c == '(' || c == ')' || c == '[' || c == ']' || c == '|' || c == '│' || c == '\t';

    // One single-element array per ASCII value, built once -- GetPinyinSegments was allocating both
    // a new string AND a new array for every character of every candidate on every DP fallback call
    // (a 30-char name costs ~90 allocations per call), which showed up as a measurable chunk of the
    // ~10us/candidate this fallback costs. ASCII's segment is always just its own lowercase form, so
    // it never varies and is safe to share.
    private static readonly string[][] AsciiLowerSegments = BuildAsciiLowerSegments();

    private static string[][] BuildAsciiLowerSegments()
    {
        var table = new string[128][];
        for (var i = 0; i < 128; i++)
            table[i] = new[] { char.ToLowerInvariant((char)i).ToString() };
        return table;
    }

    private static string[] GetPinyinSegments(char c)
    {
        if (c <= 127)
            return AsciiLowerSegments[c];

        var s = c.ToString();
        var list = new List<string>();
        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            try
            {
                if (provider.CanHandle(s))
                {
                    foreach (var alias in provider.GetAliases(s))
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
