using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using SwiftList.Core.SearchIndex.Fzf;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.SearchIndex.RecordSearch;

internal static class NameSearchExtensions
{
    public static List<SearchResult> SearchNames(
        this Searcher searcher,
        RuntimeIndex index,
        FzfPattern pattern,
        int limit,
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
            return index.Finish(new List<FzfRank>(cachedRanks), limit);
        }

        if (searcher.TrySearchIncrementalCache(index, pattern, keep, matches, slab, token, directoryFilterLower, directoryRootId))
        {
            var ranks = matches.Finish(keep);
            searcher.StoreRankCache(index, cacheTerm, ranks, limit);
            return index.Finish(ranks, limit);
        }

        var matchedCandidates = cacheable ? new List<int>(Math.Min(index.Count, 16_384)) : null;
        if (searcher.TrySearchFastInitialCandidates(index, pattern, keep, matches, token, directoryFilterLower, directoryRootId, matchedCandidates, out var completedFastSearch))
        {
            if (completedFastSearch)
                searcher.CacheManager.StoreCandidateCache(index, cacheTerm, matchedCandidates);
            var fastRanks = matches.Finish(keep);
            if (completedFastSearch)
                searcher.StoreRankCache(index, cacheTerm, fastRanks, limit);
            return index.Finish(fastRanks, limit);
        }

        SearchNameCandidates(index, pattern, keep, matches, slab, token, directoryFilterLower, directoryRootId, matchedCandidates);
        searcher.CacheManager.StoreCandidateCache(index, cacheTerm, matchedCandidates);
        var finishedRanks = matches.Finish(keep);
        searcher.StoreRankCache(index, cacheTerm, finishedRanks, limit);

        return index.Finish(finishedRanks, limit);
    }

    private static void SearchNameCandidates(
        RuntimeIndex index,
        FzfPattern pattern,
        int keep,
        FzfTopN matches,
        FzfSlab slab,
        CancellationToken token,
        string? directoryFilterLower,
        UInt128? directoryRootId,
        List<int>? matchedCandidates)
    {
        var count = index.Count;
        if (directoryFilterLower == null && directoryRootId == null && count >= Helpers.ParallelNameSearchThreshold)
        {
            SearchNameCandidatesParallel(index, pattern, keep, matches, token, matchedCandidates);
            return;
        }

        var directoryRootIndex = -1;
        if (directoryRootId != null)
        {
            if (!index.TryGetIndexById(directoryRootId.Value, out directoryRootIndex))
                return;
        }

        var directoryMembershipCache = directoryRootId != null ? new Dictionary<int, bool>() : null;
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
                var path = index.GetFullPath(i);
                if (!path.StartsWith(directoryFilterLower, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            MatchAndAdd(index, i, pattern, slab, matches, matchedCandidates);
        }
    }

    private static void SearchNameCandidatesParallel(
        RuntimeIndex index,
        FzfPattern pattern,
        int keep,
        FzfTopN matches,
        CancellationToken token,
        List<int>? matchedCandidates)
    {
        var count = index.Count;
        var queryMask = pattern.GetQueryMask(out var canFilter);
        var chunkCount = (count + Helpers.NameSearchChunkSize - 1) / Helpers.NameSearchChunkSize;
        var chunkResults = new FzfTopN[chunkCount];
        var chunkCandidates = matchedCandidates == null ? null : new List<int>[chunkCount];

        Parallel.For(
            0,
            chunkCount,
            new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8)
            },
            () => new MatcherWorkerContext(keep),
            (chunk, _, worker) =>
            {
                token.ThrowIfCancellationRequested();
                worker.Reset();
                var localCandidates = matchedCandidates == null ? null : worker.Candidates;
                var start = chunk * Helpers.NameSearchChunkSize;
                var end = Math.Min(start + Helpers.NameSearchChunkSize, count);

                var masks = CollectionsMarshal.AsSpan(index.CharMasks);
                var flags = CollectionsMarshal.AsSpan(index.Flags);
                var candidateIndex = start;

                if (canFilter && Avx2.IsSupported)
                {
                    var qMaskVec = Vector256.Create(queryMask);
                    var simdEnd = start + ((end - start) & ~3);
                    for (; candidateIndex < simdEnd; candidateIndex += 4)
                    {
                        if ((candidateIndex & 0xFF) == 0)
                            token.ThrowIfCancellationRequested();

                        var maskVec = Vector256.LoadUnsafe(ref masks[candidateIndex]);
                        var andVec = Vector256.BitwiseAnd(maskVec, qMaskVec);
                        var cmpVec = Vector256.Equals(andVec, qMaskVec);
                        var matchMask = cmpVec.ExtractMostSignificantBits();

                        if (matchMask == 0)
                            continue;

                        for (var offset = 0; offset < 4; offset++)
                        {
                            if ((matchMask & (1u << offset)) != 0)
                            {
                                var idx = candidateIndex + offset;
                                if ((((FileRecordFlags)flags[idx]) & FileRecordFlags.Deleted) != 0)
                                    continue;

                                MatchAndAdd(index, idx, pattern, worker.Slab, worker.Matches, localCandidates);
                            }
                        }
                    }
                }

                for (; candidateIndex < end; candidateIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    var i = candidateIndex;

                    if ((((FileRecordFlags)flags[i]) & FileRecordFlags.Deleted) != 0)
                        continue;

                    if (canFilter && (masks[i] & queryMask) != queryMask)
                        continue;

                    MatchAndAdd(index, i, pattern, worker.Slab, worker.Matches, localCandidates);
                }

                chunkResults[chunk] = worker.DetachMatches();
                chunkCandidates?[chunk] = worker.DetachCandidates();

                return worker;
            },
            _ => { });

        for (var i = 0; i < chunkResults.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (chunkResults[i] == null)
                continue;

            matches.AddRange(chunkResults[i].Finish(keep));
            if (chunkCandidates != null && chunkCandidates[i] != null)
                matchedCandidates!.AddRange(chunkCandidates[i]);
        }
    }

    private static void MatchAndAdd(
        RuntimeIndex index,
        int i,
        FzfPattern pattern,
        FzfSlab slab,
        FzfTopN matches,
        List<int>? matchedCandidates)
    {
        var name = index.GetName(i);
        if (string.IsNullOrEmpty(name))
            return;

        if (!pattern.TryMatch(name, out var match, FzfScoringScheme.Default, slab))
        {
            if (index.TryGetAliases(i, out var aliases))
            {
                var aliasMatched = false;
                foreach (var alias in aliases)
                {
                    if (pattern.TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default, slab))
                    {
                        if (!aliasMatched || aliasMatch.Score > match.Score)
                        {
                            aliasMatched = true;
                            match = aliasMatch;
                        }
                    }
                }

                if (!aliasMatched)
                    return;
            }
            else
            {
                return;
            }
        }

        matchedCandidates?.Add(i);
        matches.Add(FzfResultRank.ForDefaultScheme(i, name, match));
    }


}
