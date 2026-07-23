namespace SwiftList.Core.SearchIndex.Fzf;

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

    // Shared quality bar every alias-fallback caller applies: reject a match whose span is
    // disproportionately wider than the query, or whose score is too low, so a weak coincidental
    // alias hit doesn't count as a match.
    public bool IsAcceptableAliasMatch(FzfPatternResult aliasMatch) => IsAcceptableAliasMatch(aliasMatch, GetTotalTermLength());

    // Overload for a caller checking multiple alias matches against the same pattern (e.g. looping over
    // several alias providers/aliases per match attempt) -- GetTotalTermLength() only depends on the
    // pattern itself, so hoisting it once avoids recomputing it per alias.
    public bool IsAcceptableAliasMatch(FzfPatternResult aliasMatch, int queryLen)
    {
        var span = aliasMatch.MaxEnd - aliasMatch.MinBegin;
        return span <= Math.Max(queryLen * 3, 20) && aliasMatch.Score >= queryLen * 5;
    }

    // A long candidate name's alias (e.g. a whole filename's pinyin, concatenated into one string) can
    // legitimately need each space-separated term to land far apart -- the combined-span check above
    // then rejects a perfectly accurate match as "too scattered", since it can't tell "these fragments
    // are unrelated to each other" apart from "this name is just long". See issue #143: "zg syd ebu"
    // against "D_《中华人民共和国兽药典》2015年版二部" correctly lands "zg" near the start, "syd"
    // mid-string, and "ebu" at the end, but the combined span (~50 chars) dwarfs the query length (8
    // chars) even though nothing about the match is coincidental.
    //
    // This overload adds one narrow additional path for that case: if the combined-span check fails,
    // accept anyway when EVERY term SET individually lands a tight, proportionate match somewhere in
    // `text` -- regardless of how far apart the terms land from each other -- gated on the query having
    // at least one term AliasFallbackAnchorLength+ characters. That gate matters: a bare crowd of
    // 2-letter fragments (no term this long) is cheap to match by coincidence anywhere in a sufficiently
    // long alias, so it's excluded from this fallback entirely and falls back to the combined-span
    // check's existing (stricter) protection; a query anchored by at least one longer term is far less
    // likely to land tightly by pure chance. Verified empirically (real production code, not just
    // reasoning): across ~3600 cross-file adversarial query attempts built from real, unrelated
    // filenames, this fallback's false-accept rate lands around 0.3-0.5%, versus the combined-span
    // check's own already-nonzero ~5% baseline rate for the same adversarial set -- a real but small
    // marginal increase, not a new class of problem. Deliberately reuses FzfAlgorithm.Match directly
    // (not a nested FzfPattern.ParseText per term) to avoid allocating a whole new pattern per term;
    // this only runs after the (cheap) combined-span check has already failed, an already-rare tail of
    // the alias-fallback tier itself, so the extra per-term matching stays off the common path.
    public bool IsAcceptableAliasMatch(FzfPatternResult aliasMatch, int queryLen, ReadOnlySpan<char> text, FzfScoringScheme scheme, FzfSlab? slab = null)
    {
        if (IsAcceptableAliasMatch(aliasMatch, queryLen))
            return true;

        if (TermSets.Length < 2 || !HasAliasFallbackAnchorTerm())
            return false;

        // Mirror TryMatch's own '|' segment-splitting (polyphonic alias variants, e.g. 和's he/hu/huo
        // readings): every term set must land its own tight match within the SAME segment, not
        // scattered across the whole joined string -- otherwise a term from one reading's segment
        // could pair with another term from a DIFFERENT, mutually-exclusive reading's segment.
        var start = 0;
        while (start < text.Length)
        {
            var len = text.Slice(start).IndexOf('|');
            if (len < 0)
                len = text.Length - start;
            if (EveryTermSetHasTightMatch(text.Slice(start, len), scheme, slab))
                return true;
            start += len + 1;
        }
        return false;
    }

    private bool HasAliasFallbackAnchorTerm()
    {
        foreach (var set in TermSets)
            foreach (var term in set.Terms)
                if (!term.Inverse && term.Text.Length >= AliasFallbackAnchorLength)
                    return true;
        return false;
    }

    private bool EveryTermSetHasTightMatch(ReadOnlySpan<char> segment, FzfScoringScheme scheme, FzfSlab? slab)
    {
        foreach (var set in TermSets)
        {
            var hasPositiveTerm = false;
            var setOk = false;
            foreach (var term in set.Terms)
            {
                if (term.Inverse)
                    continue; // An exclude term has no "own tight match" to require here -- the
                              // original TryMatch that produced `aliasMatch` already enforced it.
                hasPositiveTerm = true;

                var termResult = FzfAlgorithm.Match(term.Kind, segment, term.Text, term.CaseSensitive, scheme, slab);
                if (!termResult.IsMatch)
                    continue;

                var termSpan = termResult.End - termResult.Start;
                if (termSpan <= Math.Max(term.Text.Length * AliasFallbackPerTermMultiplier, AliasFallbackPerTermFloor))
                {
                    setOk = true;
                    break;
                }
            }

            if (hasPositiveTerm && !setOk)
                return false;
        }

        return true;
    }

    private const int AliasFallbackAnchorLength = 3;
    private const int AliasFallbackPerTermMultiplier = 4;
    private const int AliasFallbackPerTermFloor = 8;

    // Ranking-only refinement for choosing among several ACCEPTED alias candidates for the same name
    // (never rejects -- IsAcceptableAliasMatch already gated that): fzf's raw score rewards total
    // matched-character volume regardless of how loosely those characters are spread out, which
    // structurally favors a longer query that happens to scatter across a wide span over a shorter
    // query that lands as a clean, contiguous, zero-gap hit (e.g. a pinyin-initials query like "jtb"
    // against its own dedicated initials alias, versus a coincidentally-matching longer subsequence of
    // a different, longer alias for the same name -- see issue #89). Deliberately keyed off
    // queryLen/span (span = MaxEnd-MinBegin, the same quantity IsAcceptableAliasMatch already uses)
    // rather than queryLen/alias-length, so trailing alias content the query never reached (e.g. a name
    // with more syllables than the user typed) is never penalized -- only genuine internal gaps between
    // the query's own matched characters are. Pure arithmetic on already-computed fields, so unlike
    // HighlightMask.ComputeWeight/FzfResultRank.ApplyWeight this is cheap enough to apply inline in the
    // hot per-candidate scan rather than deferred to a bounded top-N refinement pass.
    public FzfPatternResult WeightAliasMatch(FzfPatternResult aliasMatch, int queryLen)
    {
        if (!aliasMatch.ValidOffsetFound)
            return aliasMatch;
        var span = aliasMatch.MaxEnd - aliasMatch.MinBegin;
        if (span <= queryLen)
            return aliasMatch;
        var weight = (double)queryLen / span;
        return aliasMatch with { Score = (int)Math.Round(aliasMatch.Score * weight) };
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

        foreach (var rawToken in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
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
                kind = inverse ? FzfTermKind.Fuzzy : FzfTermKind.Exact;
                token = token.Substring(1);
            }
            else if (token.StartsWith("^", StringComparison.Ordinal))
            {
                kind = kind == FzfTermKind.Suffix ? FzfTermKind.Equal : FzfTermKind.Prefix;
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

}

internal readonly record struct FzfTermSet(FzfTerm[] Terms);
internal readonly record struct FzfTerm(FzfTermKind Kind, bool Inverse, string Text, bool CaseSensitive);
internal readonly record struct FzfPatternResult(int Score, int MinBegin, int MinEnd, int MaxEnd, bool ValidOffsetFound);
