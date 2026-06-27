using SwiftList.Core.SearchIndex.Fzf;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.SearchIndex.RecordSearch;

internal static class CacheExtensions
{
    public static bool TrySearchIncrementalCache(
        this Searcher searcher,
        RuntimeIndex index,
        FzfPattern pattern,
        int keep,
        FzfTopN matches,
        FzfSlab slab,
        CancellationToken token,
        string? directoryFilterLower,
        UInt128? directoryRootId)
    {
        if (!searcher.CacheManager.CanCacheCandidates(pattern, directoryFilterLower, directoryRootId, out var cacheTerm))
            return false;

        var previous = searcher.CacheManager.GetCandidateCache(index, cacheTerm);
        if (previous == null ||
            previous.Count != index.Count ||
            previous.Candidates.Length == 0 ||
            cacheTerm.Length <= previous.Term.Length ||
            !cacheTerm.StartsWith(previous.Term, StringComparison.Ordinal))
        {
            return false;
        }

        if (previous.Candidates.Length >= Helpers.NameSearchChunkSize)
        {
            if (searcher.TrySearchFastCachedCandidates(index, pattern, keep, matches, token, cacheTerm, previous.Candidates))
                return true;

            searcher.SearchCachedCandidatesParallel(index, pattern, keep, matches, token, cacheTerm, previous.Candidates);
            return true;
        }

        var nextCandidates = new List<int>(Math.Min(previous.Candidates.Length, 16_384));
        foreach (var i in previous.Candidates)
        {
            token.ThrowIfCancellationRequested();
            if ((uint)i >= (uint)index.Count || index.IsDeleted(i))
                continue;

            var name = index.GetName(i);
            if (string.IsNullOrEmpty(name))
                continue;

            if (!pattern.TryMatch(name, out var match, FzfScoringScheme.Default, slab))
            {
                if (index.TryGetAliases(i, out var aliases, out var providerIds))
                {
                    var aliasMatched = false;
                    var disabledIds = SearchContext.DisabledAliasIds;
                    for (var j = 0; j < aliases.Length; j++)
                    {
                        if (disabledIds != null && disabledIds.Contains(providerIds[j]))
                            continue;

                        var alias = aliases[j];
                        if (pattern.TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default, slab))
                        {
                            var span = aliasMatch.MaxEnd - aliasMatch.MinBegin;
                            var queryLen = pattern.GetTotalTermLength();
                            if (span > Math.Max(queryLen * 3, 20) || aliasMatch.Score < queryLen * 5)
                                continue;

                            if (!aliasMatched || aliasMatch.Score > match.Score)
                            {
                                aliasMatched = true;
                                match = aliasMatch;
                            }
                        }
                    }

                    if (!aliasMatched)
                        continue;
                }
                else
                {
                    continue;
                }
            }

            nextCandidates.Add(i);
            matches.Add(FzfResultRank.ForDefaultScheme(i, name, match));
        }

        searcher.CacheManager.StoreCandidateCache(index, cacheTerm, nextCandidates);
        return true;
    }



    public static bool TryGetRankCache(this Searcher searcher, RuntimeIndex index, string cacheTerm, int limit, out FzfRank[] ranks) => searcher.CacheManager.TryGetRankCache(index, cacheTerm, limit, out ranks);

    public static void StoreRankCache(this Searcher searcher, RuntimeIndex index, string cacheTerm, List<FzfRank> ranks, int limit) => searcher.CacheManager.StoreRankCache(index, cacheTerm, ranks, limit);
}
