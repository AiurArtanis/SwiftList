using System.Text;
using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.IndexV2;

internal readonly record struct UniqueMatch(int Uid, string Name, FzfPatternResult Match);

// Phase A of name search: match every UNIQUE name in the snapshot against the pattern -- charmask
// prefilter, then for plain fuzzy terms a zero-allocation UTF-8->char subsequence rejection (fzf's
// fuzzy IsMatch is exactly "case-folded subsequence"; V1/V2 differ only in scoring, so this never
// changes the result set), then the exact FzfPattern.TryMatch with the per-unique alias fallback
// honoring SearchContext.DisabledAliasIds. Delta rows (renamed/added, not yet folded into a unique
// name table) are matched separately -- see NameSearch.MatchDeltaRows.
internal static class SearchMatcher
{
    private sealed class Worker
    {
        public readonly FzfSlab Slab = new();
        public readonly List<UniqueMatch> Hits = new();
        public readonly List<(string Alias, byte ProviderId)> Aliases = new();
        public char[] Scratch = new char[256];
    }

    internal static List<UniqueMatch> MatchUniques(Snapshot snapshot, FzfPattern pattern)
    {
        var queryMask = pattern.GetQueryMask(out var canFilter);
        var hasSimpleTerm = pattern.TryGetSimpleFuzzyTerm(out var simpleTerm);
        var queryLen = pattern.GetTotalTermLength();
        var merged = new List<UniqueMatch>();
        var mergeLock = new object();
        const int ChunkSize = 65536;
        var chunkCount = (snapshot.UniqueCount + ChunkSize - 1) / ChunkSize;

        Parallel.For(
            0,
            Math.Max(chunkCount, 1),
            () => new Worker(),
            (chunk, _, worker) =>
            {
                var start = chunk * ChunkSize;
                var end = Math.Min(start + ChunkSize, snapshot.UniqueCount);
                var masks = snapshot.UniqueMasks;
                for (var uid = start; uid < end; uid++)
                {
                    if (canFilter && (masks[uid] & queryMask) != queryMask)
                        continue;

                    var utf8 = snapshot.UniqueNameUtf8(uid);
                    if (utf8.Length == 0)
                        continue;

                    var hasAliases = snapshot.HasAliases(uid);
                    if (hasSimpleTerm && !hasAliases)
                    {
                        if (worker.Scratch.Length < utf8.Length)
                            worker.Scratch = new char[Math.Max(utf8.Length, worker.Scratch.Length * 2)];
                        var written = Encoding.UTF8.GetChars(utf8, worker.Scratch);
                        if (!IsSubsequence(worker.Scratch.AsSpan(0, written), simpleTerm.Text, simpleTerm.CaseSensitive))
                            continue;
                    }

                    var name = snapshot.GetUniqueName(uid);
                    if (pattern.TryMatch(name, out var match, FzfScoringScheme.Default, worker.Slab))
                    {
                        worker.Hits.Add(new UniqueMatch(uid, name, match));
                    }
                    else if (hasAliases && snapshot.GetAliases(uid, worker.Aliases) > 0)
                    {
                        var disabledIds = SearchContext.DisabledAliasIds;
                        var matched = false;
                        FzfPatternResult best = default;
                        foreach (var (alias, providerId) in worker.Aliases)
                        {
                            if (disabledIds != null && disabledIds.Contains(providerId))
                                continue;
                            if (pattern.TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default, worker.Slab)
                                && pattern.IsAcceptableAliasMatch(aliasMatch, queryLen)
                                && (!matched || aliasMatch.Score > best.Score))
                            {
                                matched = true;
                                best = aliasMatch;
                            }
                        }
                        if (matched)
                            worker.Hits.Add(new UniqueMatch(uid, name, best));
                    }
                }
                return worker;
            },
            worker =>
            {
                lock (mergeLock)
                {
                    merged.AddRange(worker.Hits);
                }
            });

        return merged;
    }

    // Per-ROW match against a single base snapshot row's own name+aliases (mirrors
    // StreamingSearchExtensions.MatchCandidate) -- used by path-mode search, which scans candidate
    // rows individually rather than unique-first (directory context is per-row, not per-name).
    // aliasScratch is caller-owned so a full-table scan doesn't allocate a list per row.
    internal static bool MatchRow(Snapshot snapshot, int row, FzfPattern pattern, int queryLen, FzfSlab slab,
        List<(string Alias, byte ProviderId)> aliasScratch, out string name, out FzfPatternResult match)
    {
        name = snapshot.GetName(row);
        match = default;
        if (name.Length == 0)
            return false;
        if (pattern.TryMatch(name, out match, FzfScoringScheme.Default, slab))
            return true;

        var uid = (int)snapshot.NameIds[row];
        if (!snapshot.HasAliases(uid) || snapshot.GetAliases(uid, aliasScratch) == 0)
            return false;

        var disabledIds = SearchContext.DisabledAliasIds;
        var matched = false;
        foreach (var (alias, providerId) in aliasScratch)
        {
            if (disabledIds != null && disabledIds.Contains(providerId))
                continue;
            if (pattern.TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default, slab)
                && pattern.IsAcceptableAliasMatch(aliasMatch, queryLen)
                && (!matched || aliasMatch.Score > match.Score))
            {
                matched = true;
                match = aliasMatch;
            }
        }
        return matched;
    }

    // Mirrors the old MatchCandidate's alias fallback, honoring SearchContext.DisabledAliasIds --
    // used for delta rows (renamed/added), which carry their own precomputed alias array.
    internal static bool TryMatchNameOrAliases(FzfPattern pattern, string name, string[]? aliases, byte[]? providerIds, int queryLen, FzfSlab slab, out FzfPatternResult result)
    {
        if (pattern.TryMatch(name, out result, FzfScoringScheme.Default, slab))
            return true;
        if (aliases == null)
            return false;

        var disabledIds = SearchContext.DisabledAliasIds;
        var matched = false;
        for (var j = 0; j < aliases.Length; j++)
        {
            if (disabledIds != null && providerIds != null && j < providerIds.Length && disabledIds.Contains(providerIds[j]))
                continue;
            if (pattern.TryMatch(aliases[j], out var aliasMatch, FzfScoringScheme.Default, slab)
                && pattern.IsAcceptableAliasMatch(aliasMatch, queryLen)
                && (!matched || aliasMatch.Score > result.Score))
            {
                matched = true;
                result = aliasMatch;
            }
        }
        return matched;
    }

    private static bool IsSubsequence(ReadOnlySpan<char> text, string patternText, bool caseSensitive)
    {
        var p = 0;
        for (var i = 0; i < text.Length && p < patternText.Length; i++)
            if (FzfAlgorithm.CharsEqual(text[i], patternText[p], caseSensitive))
                p++;
        return p == patternText.Length;
    }
}
