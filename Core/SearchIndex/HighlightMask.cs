using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.SearchIndex;

// The single "final highlight result" computation, shared by App's display highlighting
// (TextHighlighter, via FuzzyMatcher.ComputeHighlightMask) and Core's ranking weight (below) -- same
// per-term fallback: literal substring (every occurrence, for display) first, then the real
// FuzzyMatchV2 backtrace run directly against the text itself (covers a plain scattered/non-contiguous
// match with zero alias involvement, e.g. "chwx" against "China_White_X" -- previously the single
// biggest cost here, since it used to fall all the way to a DP re-derivation for this very common
// case), then a cheap greedy subsequence search against each alias-provider alias, mapped back onto the
// source text (covers a CJK name matched purely through pinyin -- kept as a plain scan rather than the
// real backtrace because a polyphonic name can expand to dozens of alias candidates and a synthetic
// pinyin string has no word-boundary structure worth the real algorithm's bonus scoring; measured
// slower overall to pay its DP cost that many times per candidate for no real accuracy gain).
internal static class HighlightMask
{
    // One reusable DP scratch buffer per thread (mirrors SearchMatcher's per-worker Slab) -- a fresh
    // FzfSlab starts with zero-length backing arrays, so allocating a new one per Compute/ComputeWeight
    // call would re-grow every array on its very first use and gain nothing; caching it per thread lets
    // repeated calls across many candidates (NameSearch's bounded refinement loop, PathGate's per-
    // segment weight, ...) reuse the same already-grown buffers instead of re-allocating every time.
    [ThreadStatic]
    private static FzfSlab? _threadSlab;

    private static FzfSlab RentSlab() => _threadSlab ??= new FzfSlab();

    public static bool[] Compute(string fullText, FzfPattern pattern)
    {
        var highlights = new bool[fullText.Length];
        if (fullText.Length == 0)
            return highlights;

        var materialized = fullText;
        Mark(fullText, pattern, highlights, ref materialized, RentSlab());
        return highlights;
    }

    // Ranking-facing: same computation, but works directly off a char span -- the (common) literal and
    // direct-fuzzy tiers never materialize a string at all; a string is only built if some term needs
    // the alias-provider tier, which requires the AliasProviderRegistry/IAliasProvider string APIs.
    public static double ComputeWeight(ReadOnlySpan<char> fullText, FzfPattern pattern)
    {
        if (fullText.Length == 0)
            return 0;

        var marks = fullText.Length <= 512 ? stackalloc bool[fullText.Length] : new bool[fullText.Length];
        marks.Clear();
        string? materialized = null;
        Mark(fullText, pattern, marks, ref materialized, RentSlab());
        return ComputeWeightFromMarks(marks);
    }

    private static void Mark(ReadOnlySpan<char> fullText, FzfPattern pattern, Span<bool> highlights, ref string? materialized, FzfSlab slab)
    {
        foreach (var set in pattern.TermSets)
        {
            // Mirrors FzfPattern.TryMatchSingle's own per-set OR semantics: try every non-inverse term
            // in the set, in order, and highlight whichever one actually matches THIS candidate -- not
            // just the set's first term regardless of whether it matches at all. A multi-term OR set
            // (`a | b | c`) only ever has one term match a given candidate in practice, and it can be
            // any of them; unconditionally marking term[0] left every candidate that actually matched
            // via a later term with no highlight at all (none of term[0]'s tiers found anything to mark).
            foreach (var term in set.Terms)
            {
                if (term.Inverse)
                    continue;

                if (MarkTerm(fullText, term.Text, term.CaseSensitive, highlights, ref materialized, slab))
                    break;
            }
        }
    }

    private static bool MarkTerm(ReadOnlySpan<char> fullText, string term, bool caseSensitive, Span<bool> highlights, ref string? materialized, FzfSlab slab)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (MarkLiteralSpan(fullText, term, comparison, highlights))
            return true;

        if (FzfPositionMatcher.FuzzyMatchV2WithPositions(fullText, term, caseSensitive, FzfScoringScheme.Default, highlights, slab).IsMatch)
            return true;

        materialized ??= fullText.ToString();
        if (MarkViaAliasProviders(materialized, term, caseSensitive, highlights))
            return true;

        return MarkViaMixedQuery(materialized, term, caseSensitive, highlights);
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

    // Mirrors FuzzyMatcher.IsMatch's own alias fallback (same provider iteration, same alias/'|'
    // segment structure), mapping the matched positions back onto `text` via
    // MapAliasToSourceIndices -- so a CJK name matched only through pinyin still highlights (and
    // scores) even though the query never appears verbatim in the original text. Uses a plain greedy
    // earliest-position subsequence search per alias rather than the real FuzzyMatchV2 backtrace:
    // a polyphonic CJK name can expand to dozens of alias candidates here (PinyinAliasProvider allows
    // up to 32 combinations), and unlike a real file/folder name a synthetic pinyin string has no
    // camelCase/word-boundary structure for the real algorithm's bonus scoring to add value from -- so
    // paying its full DP cost per candidate measured slower overall than this simpler scan, for a mask
    // that (per real name/text) comes out effectively identical either way.
    private static bool MarkViaAliasProviders(string text, string term, bool caseSensitive, Span<bool> highlights)
    {
        var termLower = caseSensitive ? term : term.ToLowerInvariant();

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

                        var aliasLower = caseSensitive ? alias : alias.ToLowerInvariant();
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
                // Best-effort; fall through to the next provider rather than let one plugin's failure
                // block highlighting entirely.
            }

            if (matchedAny)
                return true;
        }

        return false;
    }

    // Mixed-alphabet fallback (a query mixing a native-script character with alias-initial letters,
    // matched against a candidate starting with that same character): only reached once both the
    // plain-alias tier above and this term's own literal/direct-fuzzy tiers have failed. Segments the term by an
    // active provider's own InputRanges/OutputRanges and, on a genuine mix, paints via
    // MixedQueryMatcher -- see its header comment for the run-by-run algorithm.
    private static bool MarkViaMixedQuery(string text, string term, bool caseSensitive, Span<bool> highlights)
    {
        if (caseSensitive)
            return false;

        var mixedTerm = MixedQueryMatcher.TrySegment(term);
        if (mixedTerm == null || !mixedTerm.Provider.CanHandle(text))
            return false;

        foreach (var aliasGroup in mixedTerm.Provider.GetAliases(text))
        {
            if (string.IsNullOrEmpty(aliasGroup))
                continue;

            foreach (var alias in aliasGroup.Split('|'))
            {
                if (string.IsNullOrEmpty(alias))
                    continue;
                if (MixedQueryMatcher.TryMatchAndHighlight(mixedTerm, text, alias, highlights))
                    return true;
            }
        }

        return false;
    }

    // Finds ANY valid subsequence alignment of `term` within `text`, returning the matched positions in
    // `text` in order, or null if no such subsequence exists. Greedy (always takes the earliest possible
    // next position), which is enough for a highlight/weight mask -- this doesn't need the optimal/
    // highest-scoring alignment, just a real one.
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
