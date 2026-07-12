using System.Collections.Concurrent;
using System.Text;
using SwiftList.Core.SearchIndex;
using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.IndexV2;

// Directory-segment verification for path mode. Replaces the old per-candidate
// "build parent path string -> Split -> re-ParseText each query segment -> live pinyin per segment"
// with: segment patterns parsed ONCE per query; ancestor ROWS walked directly (right-to-left ==
// child-to-root, the same consumption semantics as splitting the built path); each ancestor's name
// and BAKED aliases consumed zero-copy from the snapshot; and the verdict memoized per parent row,
// since every file in a directory shares it. The row walk mirrors DeltaOverlay.GetFullPath: stop at
// parent < 0 or self-parent (no orphan hop -- the delta path walk has none), skip empty names, then
// offer the SourceRoot's own segments (the built path always carried the root prefix, whose tokens
// are legitimately matchable). A parent whose chain touches live delta state (rename/override) falls
// back to verifying the delta-built path string -- rare, and exactly what the old code always did.
internal sealed class PathGate
{
    private readonly Snapshot _snapshot;
    private readonly DeltaOverlay _delta;
    private readonly string[] _querySegments;
    private readonly FzfPattern[] _segmentPatterns;
    private readonly FzfBytePattern[] _segmentBytePatterns;
    private readonly string[] _rootSegments;
    // Score > 0 = verified (<= 0 rejects, matching the old dirScore contract); Depth 0 only for a
    // bare-root parent (parent path == SourceRoot, whose trailing separator swallows the child's own).
    private readonly ConcurrentDictionary<int, (int Score, byte Depth)> _memo = new();

    public PathGate(Snapshot snapshot, DeltaOverlay delta, string dirQuery)
    {
        _snapshot = snapshot;
        _delta = delta;
        _querySegments = dirQuery.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        _segmentPatterns = new FzfPattern[_querySegments.Length];
        _segmentBytePatterns = new FzfBytePattern[_querySegments.Length];
        for (var i = 0; i < _querySegments.Length; i++)
        {
            _segmentPatterns[i] = FzfPattern.ParseText(_querySegments[i]);
            _segmentBytePatterns[i] = FzfBytePattern.From(_segmentPatterns[i]);
        }
        _rootSegments = snapshot.SourceRoot.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public (int Score, byte Depth) Verify(int parentRow, SearchMatcher.Worker worker)
    {
        if (_memo.TryGetValue(parentRow, out var cached))
            return cached;

        var q = _querySegments.Length - 1;
        var score = 0;
        var sawSegment = false;
        var current = parentRow;
        for (var depth = 0; depth < 512 && current >= 0; depth++)
        {
            if (_delta.IsSuperseded(current))
            {
                // Renamed/overridden ancestor: this chain's live names come from delta state -- verify
                // the delta-built path string instead, like the old per-candidate path always did.
                var parentPath = _delta.GetFullPath(parentRow);
                var result = (VerifyPath(parentPath, worker), parentPath.EndsWith('\\') ? (byte)0 : (byte)1);
                _memo[parentRow] = result;
                return result;
            }

            var uid = (int)_snapshot.NameIds[current];
            var nameUtf8 = _snapshot.UniqueNameUtf8(uid);
            if (nameUtf8.Length > 0)
            {
                sawSegment = true;
                if (q >= 0 && TryMatchSegmentRow(uid, nameUtf8, q, worker, out var segScore))
                {
                    score += segScore;
                    q--;
                }
            }

            var parent = _snapshot.ParentIndexes[current];
            if (parent == current)
                break;
            current = parent;
        }

        for (var i = _rootSegments.Length - 1; i >= 0 && q >= 0; i--)
        {
            if (TryMatchSegmentText(_rootSegments[i], q, worker, out var segScore))
            {
                score += segScore;
                q--;
            }
        }

        var verdict = (q < 0 ? score : 0, sawSegment ? (byte)1 : (byte)0);
        _memo[parentRow] = verdict;
        return verdict;
    }

    // String-segment verification for delta rows' parent paths (and delta-touched ancestor chains):
    // same right-to-left consumption over a split path, but with the patterns parsed once per query.
    public int VerifyPath(string path, SearchMatcher.Worker worker)
    {
        var pathSegments = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var qIdx = _querySegments.Length - 1;
        var pIdx = pathSegments.Length - 1;
        var totalScore = 0;

        while (qIdx >= 0 && pIdx >= 0)
        {
            if (TryMatchSegmentText(pathSegments[pIdx], qIdx, worker, out var score))
            {
                totalScore += score;
                qIdx--;
            }
            pIdx--;
        }
        return qIdx < 0 ? totalScore : 0;
    }

    private bool TryMatchSegmentRow(int uid, ReadOnlySpan<byte> nameUtf8, int q, SearchMatcher.Worker worker, out int score)
    {
        score = 0;
        if (_snapshot.IsUniqueAscii(uid))
        {
            if (_segmentBytePatterns[q].TryMatch(nameUtf8, out var match, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers))
            {
                score = match.Score;
                return true;
            }
        }
        else
        {
            if (worker.Scratch.Length < nameUtf8.Length)
                worker.Scratch = new char[Math.Max(nameUtf8.Length, worker.Scratch.Length * 2)];
            var written = Encoding.UTF8.GetChars(nameUtf8, worker.Scratch);
            if (_segmentPatterns[q].TryMatch(worker.Scratch.AsSpan(0, written), out var match, FzfScoringScheme.Default, worker.Slab))
            {
                score = match.Score;
                return true;
            }
        }

        // Baked-alias fallback: deliberately UNGATED (no IsAcceptableAliasMatch) and first-match-wins,
        // preserving the old TryMatchSegmentWithAlias semantics -- which regenerated pinyin LIVE per
        // candidate; the same aliases now come zero-copy from the snapshot.
        var disabledIds = SearchContext.DisabledAliasIds;
        var (start, end) = _snapshot.AliasEntryRange(uid);
        for (var e = start; e < end; e++)
        {
            if (disabledIds != null && disabledIds.Contains(_snapshot.AliasProviderId(e)))
                continue;
            var aliasUtf8 = _snapshot.AliasUtf8(e);
            if (aliasUtf8.Length == 0)
                continue;
            if (Ascii.IsValid(aliasUtf8))
            {
                if (_segmentBytePatterns[q].TryMatchSegmented(aliasUtf8, out var aliasMatch, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers))
                {
                    score = aliasMatch.Score;
                    return true;
                }
            }
            else
            {
                if (worker.AliasScratch.Length < aliasUtf8.Length)
                    worker.AliasScratch = new char[Math.Max(aliasUtf8.Length, worker.AliasScratch.Length * 2)];
                var written = Encoding.UTF8.GetChars(aliasUtf8, worker.AliasScratch);
                if (_segmentPatterns[q].TryMatch(worker.AliasScratch.AsSpan(0, written), out var aliasMatch, FzfScoringScheme.Default, worker.Slab))
                {
                    score = aliasMatch.Score;
                    return true;
                }
            }
        }
        return false;
    }

    // Segments that only exist as text (SourceRoot tokens, delta-path segments) -- no baked aliases
    // to consult, so non-ASCII segments fall back to live alias generation, as the old code did.
    private bool TryMatchSegmentText(string segment, int q, SearchMatcher.Worker worker, out int score)
    {
        score = 0;
        if (_segmentPatterns[q].TryMatch(segment, out var match, FzfScoringScheme.Default, worker.Slab))
        {
            score = match.Score;
            return true;
        }
        if (!AliasProviderRegistry.HasNonAscii(segment))
            return false;
        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            try
            {
                if (!provider.CanHandle(segment))
                    continue;
                foreach (var alias in provider.GetAliases(segment))
                {
                    if (_segmentPatterns[q].TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default, worker.Slab))
                    {
                        score = aliasMatch.Score;
                        return true;
                    }
                }
            }
            catch
            {
            }
        }
        return false;
    }

    // Ranking-only weight (percentage*consecutiveness, product across matched segments), computed
    // separately from Verify/VerifyPath above and ONLY for path-mode's bounded post-scan refinement
    // (PathSearchFuzzy) -- NOT memoized, NOT called during the hot scan, since it needs the same
    // relatively expensive HighlightMask computation name mode moved out of its own hot path for the
    // same reason (see FzfResultRank.ApplyWeight). Re-walks the same ancestor chain as Verify; safe to
    // call only on the small headroom-bounded candidate set that survives the unweighted scan.
    public double ComputeWeight(int parentRow, SearchMatcher.Worker worker)
    {
        var q = _querySegments.Length - 1;
        var weight = 1.0;
        var current = parentRow;
        for (var depth = 0; depth < 512 && current >= 0 && q >= 0; depth++)
        {
            if (_delta.IsSuperseded(current))
            {
                var parentPath = _delta.GetFullPath(parentRow);
                return weight * ComputeWeightForPath(parentPath, worker);
            }

            var uid = (int)_snapshot.NameIds[current];
            var nameUtf8 = _snapshot.UniqueNameUtf8(uid);
            if (nameUtf8.Length > 0 && TryMatchSegmentRowWeight(uid, nameUtf8, q, worker, out var segWeight))
            {
                weight *= segWeight;
                q--;
            }

            var parent = _snapshot.ParentIndexes[current];
            if (parent == current)
                break;
            current = parent;
        }

        for (var i = _rootSegments.Length - 1; i >= 0 && q >= 0; i--)
        {
            if (TryMatchSegmentTextWeight(_rootSegments[i], q, worker, out var segWeight))
            {
                weight *= segWeight;
                q--;
            }
        }

        return weight;
    }

    public double ComputeWeightForPath(string path, SearchMatcher.Worker worker)
    {
        var pathSegments = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var qIdx = _querySegments.Length - 1;
        var pIdx = pathSegments.Length - 1;
        var weight = 1.0;

        while (qIdx >= 0 && pIdx >= 0)
        {
            if (TryMatchSegmentTextWeight(pathSegments[pIdx], qIdx, worker, out var segWeight))
            {
                weight *= segWeight;
                qIdx--;
            }
            pIdx--;
        }
        return weight;
    }

    // Mirrors TryMatchSegmentRow's match-finding exactly, but only needs the winning branch's
    // weight -- re-running TryMatch here (rather than threading weight through the score-only method)
    // keeps the hot Verify/TryMatchSegmentRow path free of any HighlightMask reference at all.
    private bool TryMatchSegmentRowWeight(int uid, ReadOnlySpan<byte> nameUtf8, int q, SearchMatcher.Worker worker, out double weight)
    {
        weight = 1.0;
        var pattern = _segmentPatterns[q];
        if (_snapshot.IsUniqueAscii(uid))
        {
            if (_segmentBytePatterns[q].TryMatch(nameUtf8, out _, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers))
            {
                weight = FzfBytePattern.ComputeWeight(nameUtf8, pattern);
                return true;
            }
        }
        else
        {
            if (worker.Scratch.Length < nameUtf8.Length)
                worker.Scratch = new char[Math.Max(nameUtf8.Length, worker.Scratch.Length * 2)];
            var written = Encoding.UTF8.GetChars(nameUtf8, worker.Scratch);
            var name = worker.Scratch.AsSpan(0, written);
            if (pattern.TryMatch(name, out _, FzfScoringScheme.Default, worker.Slab))
            {
                weight = HighlightMask.ComputeWeight(name, pattern);
                return true;
            }
        }

        var disabledIds = SearchContext.DisabledAliasIds;
        var (start, end) = _snapshot.AliasEntryRange(uid);
        for (var e = start; e < end; e++)
        {
            if (disabledIds != null && disabledIds.Contains(_snapshot.AliasProviderId(e)))
                continue;
            var aliasUtf8 = _snapshot.AliasUtf8(e);
            if (aliasUtf8.Length == 0)
                continue;
            var isMatch = Ascii.IsValid(aliasUtf8)
                ? _segmentBytePatterns[q].TryMatchSegmented(aliasUtf8, out _, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers)
                : MatchesAliasChars(pattern, aliasUtf8, worker);
            if (isMatch)
            {
                // Weight is measured against the segment's own display name, not the alias string --
                // mirrors HighlightMask, which maps alias-matched positions back onto the source name.
                weight = ComputeSegmentNameWeight(uid, nameUtf8, worker, pattern);
                return true;
            }
        }
        return false;
    }

    private static bool MatchesAliasChars(FzfPattern pattern, ReadOnlySpan<byte> aliasUtf8, SearchMatcher.Worker worker)
    {
        if (worker.AliasScratch.Length < aliasUtf8.Length)
            worker.AliasScratch = new char[Math.Max(aliasUtf8.Length, worker.AliasScratch.Length * 2)];
        var written = Encoding.UTF8.GetChars(aliasUtf8, worker.AliasScratch);
        return pattern.TryMatch(worker.AliasScratch.AsSpan(0, written), out _, FzfScoringScheme.Default, worker.Slab);
    }

    private double ComputeSegmentNameWeight(int uid, ReadOnlySpan<byte> nameUtf8, SearchMatcher.Worker worker, FzfPattern pattern)
    {
        if (_snapshot.IsUniqueAscii(uid))
            return FzfBytePattern.ComputeWeight(nameUtf8, pattern);

        if (worker.Scratch.Length < nameUtf8.Length)
            worker.Scratch = new char[Math.Max(nameUtf8.Length, worker.Scratch.Length * 2)];
        var written = Encoding.UTF8.GetChars(nameUtf8, worker.Scratch);
        return HighlightMask.ComputeWeight(worker.Scratch.AsSpan(0, written), pattern);
    }

    private bool TryMatchSegmentTextWeight(string segment, int q, SearchMatcher.Worker worker, out double weight)
    {
        weight = 1.0;
        var pattern = _segmentPatterns[q];
        if (pattern.TryMatch(segment, out _, FzfScoringScheme.Default, worker.Slab))
        {
            weight = pattern.IsEmpty ? 1.0 : HighlightMask.ComputeWeight(segment, pattern);
            return true;
        }
        if (!AliasProviderRegistry.HasNonAscii(segment))
            return false;
        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            try
            {
                if (!provider.CanHandle(segment))
                    continue;
                foreach (var alias in provider.GetAliases(segment))
                {
                    if (pattern.TryMatch(alias, out _, FzfScoringScheme.Default, worker.Slab))
                    {
                        weight = HighlightMask.ComputeWeight(segment, pattern);
                        return true;
                    }
                }
            }
            catch
            {
            }
        }
        return false;
    }
}
