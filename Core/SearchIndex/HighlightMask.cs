using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.SearchIndex;

// The single "final highlight result" computation, shared by App's display highlighting
// (TextHighlighter, via FuzzyMatcher.ComputeHighlightMask) and Core's ranking weight (below) --
// same 3-tier fallback per term: literal substring, then alias-provider mapped positions (handles a
// CJK name matched purely through pinyin, with zero literal character overlap with the query), then
// FuzzyHighlightMatcher's DP fallback. Moved out of App/Converters/HighlightConverter.cs so ranking
// can compute the exact same mask a user would see highlighted, rather than a cheaper independent
// approximation that could disagree with it (e.g. score a pinyin-only match as 0% covered).
internal static class HighlightMask
{
    public static bool[] Compute(string fullText, FzfPattern pattern)
    {
        var highlights = new bool[fullText.Length];
        if (fullText.Length == 0)
            return highlights;

        var fullTextLower = fullText.ToLowerInvariant();

        foreach (var set in pattern.TermSets)
        {
            // First non-inverse term in the set that actually matches -- mirrors FzfPattern's own
            // "first successful term wins" per-set semantics (see TryMatchSingle's `best`).
            foreach (var term in set.Terms)
            {
                if (term.Inverse)
                    continue;

                MarkTerm(fullText, fullTextLower, term.Text, term.CaseSensitive, highlights);
                break;
            }
        }

        return highlights;
    }

    // Ranking-facing: same 3-tier computation, but works directly off a char span with no per-
    // candidate string allocation in the common case -- tier 1 (literal substring) runs entirely on
    // spans, and a lowercased string is only materialized if some term needs the alias/DP fallback
    // tiers (which require the AliasProviderRegistry/FuzzyHighlightMatcher string-based APIs).
    public static double ComputeWeight(ReadOnlySpan<char> fullText, FzfPattern pattern)
    {
        if (fullText.Length == 0)
            return 0;

        Span<bool> marks = fullText.Length <= 512 ? stackalloc bool[fullText.Length] : new bool[fullText.Length];
        marks.Clear();
        string? fullTextLower = null;

        foreach (var set in pattern.TermSets)
        {
            foreach (var term in set.Terms)
            {
                if (term.Inverse)
                    continue;

                var comparison = term.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var foundAny = MarkLiteralSpan(fullText, term.Text, comparison, marks);

                if (!foundAny)
                {
                    fullTextLower ??= fullText.ToString().ToLowerInvariant();
                    var termLower = term.CaseSensitive ? term.Text.ToLowerInvariant() : term.Text;
                    if (!TryHighlightViaAliasProviders(fullTextLower, termLower, marks))
                        FuzzyHighlightMatcher.MarkFuzzyMatch(fullTextLower, termLower, marks);
                }

                break;
            }
        }

        return ComputeWeightFromMarks(marks);
    }

    private static void MarkTerm(string fullText, string fullTextLower, string term, bool caseSensitive, Span<bool> highlights)
    {
        var haystack = caseSensitive ? fullText : fullTextLower;
        var foundAny = MarkLiteralSpan(haystack, term, StringComparison.Ordinal, highlights);

        if (!foundAny)
        {
            var termLower = caseSensitive ? term.ToLowerInvariant() : term;
            if (!TryHighlightViaAliasProviders(fullText, termLower, highlights))
            {
                FuzzyHighlightMatcher.MarkFuzzyMatch(fullTextLower, termLower, highlights);
            }
        }
    }

    private static bool MarkLiteralSpan(ReadOnlySpan<char> haystack, ReadOnlySpan<char> needle, StringComparison comparison, Span<bool> highlights)
    {
        if (needle.Length == 0)
            return false;

        var foundAny = false;
        var startIdx = 0;
        while (startIdx < haystack.Length)
        {
            var idx = haystack.Slice(startIdx).IndexOf(needle, comparison);
            if (idx < 0)
                break;

            var absolute = startIdx + idx;
            for (var i = absolute; i < absolute + needle.Length && i < highlights.Length; i++)
                highlights[i] = true;

            foundAny = true;
            startIdx = absolute + 1;
        }

        return foundAny;
    }

    // Corrected ranking-weight formula (percentage of the WHOLE candidate string that's covered,
    // then weighted by how contiguous that coverage is): weight = percentage * consecutiveness.
    // Both factors are <= 1, so this only ever demotes a match relative to its raw score -- it's a
    // ranking multiplier, never a gate; a candidate that already passed the real fzf match always
    // stays a match regardless of this weight.
    private static double ComputeWeightFromMarks(ReadOnlySpan<bool> mask)
    {
        if (mask.Length == 0)
            return 0;

        var matchedLength = 0;
        var sumOfSquares = 0L;
        var runLength = 0;
        foreach (var m in mask)
        {
            if (m)
            {
                matchedLength++;
                runLength++;
            }
            else if (runLength > 0)
            {
                sumOfSquares += (long)runLength * runLength;
                runLength = 0;
            }
        }
        if (runLength > 0)
            sumOfSquares += (long)runLength * runLength;

        if (matchedLength == 0)
            return 0;

        var percentage = (double)matchedLength / mask.Length;
        var consecutiveness = (double)sumOfSquares / ((long)matchedLength * matchedLength);
        return percentage * consecutiveness;
    }

    // Mirrors the per-term text->alias->source-index mapping the real name/alias match used, so a
    // CJK name matched only through an alias (e.g. pinyin) still highlights (and scores) correctly.
    private static bool TryHighlightViaAliasProviders(string text, string termLower, Span<bool> highlights)
    {
        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            var matchedAny = false;
            try
            {
                if (!provider.CanHandle(text))
                    continue;

                foreach (var aliasGroup in provider.GetAliases(text))
                {
                    if (string.IsNullOrEmpty(aliasGroup))
                        continue;

                    foreach (var alias in aliasGroup.Split('|'))
                    {
                        if (string.IsNullOrEmpty(alias))
                            continue;

                        var aliasLower = alias.ToLowerInvariant();
                        var positions = FindSubsequencePositions(aliasLower, termLower);
                        if (positions == null)
                            continue;

                        var map = provider.MapAliasToSourceIndices(text, alias);
                        if (map == null || map.Length != alias.Length)
                            continue;

                        foreach (var aliasPos in positions)
                        {
                            if (aliasPos < 0 || aliasPos >= map.Length)
                                continue;
                            var sourceIndex = map[aliasPos];
                            if (sourceIndex >= 0 && sourceIndex < highlights.Length)
                                highlights[sourceIndex] = true;
                        }

                        matchedAny = true;
                    }
                }
            }
            catch
            {
                // Best-effort; fall through to the next provider (or the FuzzyHighlightMatcher
                // fallback) rather than let one plugin's failure block highlighting entirely.
            }

            if (matchedAny)
                return true;
        }

        return false;
    }

    // Finds ANY valid subsequence alignment of `term` within `text` (both already lowercased),
    // returning the matched positions in `text` in order, or null if no such subsequence exists.
    // Greedy (always takes the earliest possible next position), which is enough for a highlight --
    // this doesn't need the optimal/highest-scoring alignment, just a real one.
    private static int[]? FindSubsequencePositions(string text, string term)
    {
        if (term.Length == 0)
            return null;

        var positions = new int[term.Length];
        var searchFrom = 0;
        for (var i = 0; i < term.Length; i++)
        {
            var idx = text.IndexOf(term[i], searchFrom);
            if (idx < 0)
                return null;
            positions[i] = idx;
            searchFrom = idx + 1;
        }

        return positions;
    }
}
