using SwiftList.Core.SearchIndex.Fzf;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.SearchIndex.RecordSearch;

internal static class StreamingSearchExtensions
{
    internal static bool MatchCandidate(
        RuntimeIndex index,
        int i,
        FzfPattern pattern,
        FzfSlab slab,
        out string name,
        out FzfPatternResult match)
    {
        name = index.GetName(i);
        match = default;
        if (string.IsNullOrEmpty(name))
            return false;

        if (!pattern.TryMatch(name, out match, FzfScoringScheme.Default, slab))
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
                    return false;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    public static void SearchNamesStreaming(
        this Searcher searcher,
        RuntimeIndex index,
        FzfPattern pattern,
        int limit,
        Action<SearchResult> onResult,
        CancellationToken token,
        string? directoryFilterLower,
        UInt128? directoryRootId)
    {
        var keep = Math.Max(limit * 8, 64);
        var matches = new FzfTopN(keep);
        var slab = new FzfSlab();
        var cacheable = searcher.CacheManager.CanCacheCandidates(pattern, directoryFilterLower, directoryRootId, out var cacheTerm);

        if (cacheable && searcher.TryGetRankCache(index, cacheTerm, limit, out var cachedRanks))
        {
            foreach (var r in index.Finish(new List<FzfRank>(cachedRanks), limit))
                onResult(r);
            return;
        }

        var streamedCount = 0;
        var streamLock = new object();
        Action<int, string, FzfPatternResult> streamCallback = (entryIndex, name, match) =>
        {
            if (Volatile.Read(ref streamedCount) >= limit) return;
            lock (streamLock)
            {
                if (streamedCount >= limit) return;
                streamedCount++;
                var flags = (FileRecordFlags)index.Flags[entryIndex];
                var res = new SearchResult
                {
                    Name = name,
                    Path = index.GetFullPath(entryIndex),
                    IsDir = index.IsDirectory(entryIndex),
                    Drive = index.SourceKey,
                    Attributes = FileRecordFlagsHelper.ToAttributes(flags),
                    RankSortKey = FzfResultRank.ForDefaultScheme(entryIndex, name, match).SortKey,
                    ModifiedUtc = index.GetLastWriteTimeUnixSeconds(entryIndex)
                };
                onResult(res);
            }
        };

        if (searcher.TrySearchIncrementalCache(index, pattern, keep, matches, slab, token, directoryFilterLower, directoryRootId))
        {
            var ranks = matches.Finish(keep);
            searcher.StoreRankCache(index, cacheTerm, ranks, limit);
            foreach (var r in index.Finish(ranks, limit))
            {
                token.ThrowIfCancellationRequested();
                onResult(r);
            }
            return;
        }

        var matchedCandidates = cacheable ? new List<int>(Math.Min(index.Count, 16_384)) : null;
        SearchNameCandidatesStreaming(index, pattern, keep, matches, slab, streamCallback, token, directoryFilterLower, directoryRootId, matchedCandidates, searcher.MaxDegreeOfParallelism);
        searcher.CacheManager.StoreCandidateCache(index, cacheTerm, matchedCandidates);
        var finishedRanks = matches.Finish(keep);
        searcher.StoreRankCache(index, cacheTerm, finishedRanks, limit);
        foreach (var r in index.Finish(finishedRanks, limit))
        {
            token.ThrowIfCancellationRequested();
            onResult(r);
        }
    }

    private static void SearchNameCandidatesStreaming(
        RuntimeIndex index,
        FzfPattern pattern,
        int keep,
        FzfTopN matches,
        FzfSlab slab,
        Action<int, string, FzfPatternResult> streamCallback,
        CancellationToken token,
        string? directoryFilterLower,
        UInt128? directoryRootId,
        List<int>? matchedCandidates,
        int maxDegreeOfParallelism)
    {
        var count = index.Count;
        if (directoryFilterLower == null && directoryRootId == null && count >= Helpers.ParallelNameSearchThreshold)
        {
            ParallelStreamingSearchExtensions.SearchNameCandidatesParallelStreaming(index, pattern, keep, matches, streamCallback, token, matchedCandidates, maxDegreeOfParallelism);
            return;
        }

        var directoryRootIndex = -1;
        if (directoryRootId != null)
        {
            if (!index.TryGetIndexById(directoryRootId.Value, out directoryRootIndex))
                return;
        }

        var filterAncestorIndex = -1;
        if (directoryFilterLower != null &&
            index.TryResolvePath(directoryFilterLower, out var ancestorId, out var childPrefix) &&
            index.TryGetIndexById(ancestorId, out var ancestorIndex))
        {
            filterAncestorIndex = ancestorIndex;
        }

        var directoryMembershipCache = (directoryRootId != null || directoryFilterLower != null) ? new Dictionary<int, bool>() : null;
        var queryMask = pattern.GetQueryMask(out var canFilter);

        for (var candidateIndex = 0; candidateIndex < count; candidateIndex++)
        {
            token.ThrowIfCancellationRequested();
            var i = candidateIndex;

            if (index.IsDeleted(i))
                continue;

            if (canFilter && (index.CharMasks[i] & queryMask) != queryMask)
                continue;

            if (directoryRootIndex >= 0)
            {
                if (!index.IsUnderDirectoryCached(i, directoryRootIndex, directoryMembershipCache!))
                    continue;
            }
            else if (directoryFilterLower != null)
            {
                if (filterAncestorIndex >= 0 &&
                    !index.IsUnderDirectoryCached(i, filterAncestorIndex, directoryMembershipCache!))
                {
                    continue;
                }

                var path = index.GetFullPath(i);
                if (!path.StartsWith(directoryFilterLower, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            if (MatchCandidate(index, i, pattern, slab, out var name, out var match))
            {
                matchedCandidates?.Add(i);
                matches.Add(FzfResultRank.ForDefaultScheme(i, name, match));
                streamCallback(i, name, match);
            }
        }
    }
}
