namespace SwiftList.Core.SearchIndex.Fzf;

// Alias-fallback quality-gating (IsAcceptableAliasMatch/WeightAliasMatch and their private helpers)
// lives in FzfPatternAliasMatchExtensions.cs (extension methods, matching TreeBuilder's Checkpoint/Diff
// split and MenuBuilder's ContentExtensions split) instead of a partial class, to keep this file under
// the project's line limit. This file keeps pattern parsing (Parse/ParseText/ParseTermSets) and the core
// text-matching algorithm (TryMatch/TryMatchSingle).
internal sealed class FzfPattern
{
    private FzfPattern(string? targetDrive, FzfTermSet[] termSets)
    {
        TargetDrive = targetDrive;
        TermSets = termSets;
    }

    public string? TargetDrive { get; }
    public FzfTermSet[] TermSets { get; }
    public bool IsEmpty => TermSets.Length == 0;

    public int GetTotalTermLength()
    {
        var len = 0;
        foreach (var set in TermSets)
        {
            foreach (var term in set.Terms)
            {
                if (!term.Inverse)
                    len += term.Text.Length;
            }
        }
        return len;
    }

    public static FzfPattern Parse(string query)
    {
        string? targetDrive = null;
        var terms = new List<string>();
        foreach (var rawTerm in query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawTerm.Length >= 2 && char.IsLetter(rawTerm[0]) && rawTerm[1] == Path.VolumeSeparatorChar)
            {
                targetDrive = rawTerm[0].ToString();
                continue;
            }

            terms.Add(rawTerm);
        }

        return new FzfPattern(targetDrive, ParseTermSets(string.Join(' ', terms)));
    }

    public static FzfPattern ParseText(string query) => new FzfPattern(null, ParseTermSets(query));

    public bool TryMatch(ReadOnlySpan<char> text, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab = null)
    {
        if (text.Contains('|'))
        {
            // ponytail: handle polyphonic aliases by matching each segment independently to prevent
            // incorrect cross-boundary match failure. Slicing (not Substring) keeps this allocation-free.
            var bestResult = default(FzfPatternResult);
            var matchedAny = false;
            var start = 0;
            while (start < text.Length)
            {
                var len = text.Slice(start).IndexOf('|');
                if (len < 0)
                    len = text.Length - start;

                if (TryMatchSingle(text.Slice(start, len), out var segmentResult, scheme, slab))
                {
                    if (segmentResult.ValidOffsetFound)
                    {
                        segmentResult = new FzfPatternResult(
                            segmentResult.Score,
                            segmentResult.MinBegin + start,
                            segmentResult.MinEnd + start,
                            segmentResult.MaxEnd + start,
                            true
                        );
                    }

                    if (!matchedAny || segmentResult.Score > bestResult.Score)
                    {
                        bestResult = segmentResult;
                        matchedAny = true;
                    }
                }

                start += len + 1;
            }

            result = bestResult;
            return matchedAny;
        }

        return TryMatchSingle(text, out result, scheme, slab);
    }

    // Text never contains '|' here: the segmented branch above slices it away, and real file names
    // can't contain it (invalid in Windows paths) -- so no cross-'|' span check is needed.
    private bool TryMatchSingle(ReadOnlySpan<char> text, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab = null)
    {
        var totalScore = 0;
        var minBegin = int.MaxValue;
        var minEnd = int.MaxValue;
        var maxEnd = 0;
        var validOffsetFound = false;

        foreach (var set in TermSets)
        {
            var matched = false;
            FzfMatchResult best = default;
            foreach (var term in set.Terms)
            {
                var current = FzfAlgorithm.Match(term.Kind, text, term.Text, term.CaseSensitive, scheme, slab);
                if (current.IsMatch)
                {
                    if (term.Inverse)
                    {
                        matched = false;
                        best = default;
                        break;
                    }

                    matched = true;
                    best = current;
                    break;
                }

                if (term.Inverse)
                {
                    matched = true;
                    best = new FzfMatchResult(0, 0, 0);
                }
            }

            if (!matched)
            {
                result = default;
                return false;
            }

            totalScore += best.Score;
            if (best.Start < best.End)
            {
                minBegin = Math.Min(minBegin, best.Start);
                minEnd = Math.Min(minEnd, best.End);
                maxEnd = Math.Max(maxEnd, best.End);
                validOffsetFound = true;
            }
        }

        result = new FzfPatternResult(totalScore, minBegin, minEnd, maxEnd, validOffsetFound);
        return true;
    }

    private static FzfTermSet[] ParseTermSets(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<FzfTermSet>();

        query = query.Replace("\\ ", "\t");
        var sets = new List<FzfTermSet>();
        var current = new List<FzfTerm>();
        var switchSet = false;
        var afterBar = false;

        foreach (var rawToken in MergeQuotedPhrases(query.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
        {
            var token = rawToken.Replace('\t', ' ');
            if (current.Count > 0 && !afterBar && token == "|")
            {
                switchSet = false;
                afterBar = true;
                continue;
            }

            afterBar = false;
            var kind = FzfTermKind.Fuzzy;
            var inverse = false;
            if (token.StartsWith("!", StringComparison.Ordinal))
            {
                inverse = true;
                kind = FzfTermKind.Exact;
                token = token.Substring(1);
            }

            if (token != "$" && token.EndsWith("$", StringComparison.Ordinal))
            {
                kind = FzfTermKind.Suffix;
                token = token.Substring(0, token.Length - 1);
            }

            if (token.Length > 2 && token.StartsWith("'", StringComparison.Ordinal) && token.EndsWith("'", StringComparison.Ordinal))
            {
                kind = FzfTermKind.ExactBoundary;
                token = token.Substring(1, token.Length - 2);
            }
            else if (token.StartsWith("'", StringComparison.Ordinal))
            {
                // A trailing "$" already claimed the kind ("'foo$"). A suffix match is exact by
                // nature, so the "'" adds nothing there and must not overwrite Suffix -- doing so
                // silently discarded the end anchor the user explicitly typed.
                if (kind != FzfTermKind.Suffix)
                    kind = inverse ? FzfTermKind.Fuzzy : FzfTermKind.Exact;
                token = token.Substring(1);
            }
            else if (token.StartsWith("^", StringComparison.Ordinal))
            {
                kind = kind == FzfTermKind.Suffix ? FzfTermKind.Equal : FzfTermKind.Prefix;
                token = token.Substring(1);
                // "^'abc": Prefix/Equal are already exact, so a "'" here is a redundant operator
                // rather than text. Left in, it searched for a literal apostrophe no name contains.
                if (token.StartsWith("'", StringComparison.Ordinal))
                    token = token.Substring(1);
            }

            if (token.Length == 0)
                continue;

            if (switchSet)
            {
                sets.Add(new FzfTermSet(current.ToArray()));
                current.Clear();
            }

            var lower = token.ToLowerInvariant();
            var caseSensitive = token != lower;
            current.Add(new FzfTerm(kind, inverse, caseSensitive ? token : lower, caseSensitive));
            switchSet = true;
        }

        if (current.Count > 0)
            sets.Add(new FzfTermSet(current.ToArray()));

        return sets.ToArray();
    }

    // Reassembles a quoted phrase whose content contains spaces ("'cad acb'"). Necessary because the
    // split above runs BEFORE any operator parsing: such a query otherwise became the two unrelated
    // terms Exact("cad") and Fuzzy("acb'"), the second searching for a literal apostrophe no real name
    // contains, so the whole query could never match anything -- which is what the documented
    // "'final report'" form actually did.
    //
    // Merging is deliberately gated on the quotes sitting at token BOUNDARIES: an opening quote that
    // starts a token (after an optional "!"), a closing quote that ends a later one. An apostrophe
    // mid-word therefore never opens a phrase, leaving an ordinary query like "don't stop" untouched.
    // The lookahead also stops at a bare "|", so an OR of two quoted terms ("'foo | 'bar'") keeps
    // parsing as an OR instead of collapsing into one phrase that swallows the separator.
    private static List<string> MergeQuotedPhrases(string[] tokens)
    {
        var merged = new List<string>(tokens.Length);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var open = QuoteStartIndex(token);
            if (open < 0 || IsSelfClosingQuote(token, open))
            {
                merged.Add(token);
                continue;
            }

            var close = -1;
            for (var j = i + 1; j < tokens.Length; j++)
            {
                if (tokens[j] == "|")
                    break;
                if (tokens[j].EndsWith("'", StringComparison.Ordinal))
                {
                    close = j;
                    break;
                }
            }

            if (close < 0)
            {
                merged.Add(token); // unmatched opening quote: leave the old term-by-term reading alone
                continue;
            }

            merged.Add(string.Join(' ', tokens, i, close - i + 1));
            i = close;
        }
        return merged;
    }

    // Index of a phrase-opening "'" (0, or 1 when the token is negated with "!"), or -1 for none.
    private static int QuoteStartIndex(string token)
    {
        if (token.StartsWith("'", StringComparison.Ordinal))
            return 0;
        return token.Length > 1 && token[0] == '!' && token[1] == '\'' ? 1 : -1;
    }

    // "'read'" / "!'read'" already carry their own closing quote, so they need no lookahead.
    private static bool IsSelfClosingQuote(string token, int open)
        => token.Length > open + 2 && token.EndsWith("'", StringComparison.Ordinal);
}

internal readonly record struct FzfTermSet(FzfTerm[] Terms);
internal readonly record struct FzfTerm(FzfTermKind Kind, bool Inverse, string Text, bool CaseSensitive);
internal readonly record struct FzfPatternResult(int Score, int MinBegin, int MinEnd, int MaxEnd, bool ValidOffsetFound);
