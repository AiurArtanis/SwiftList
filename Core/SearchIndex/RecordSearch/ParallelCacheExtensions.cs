using SwiftList.Core.SearchIndex.Fzf;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.SearchIndex.RecordSearch;

internal static class ParallelCacheExtensions
{
    internal static bool TrySearchFastCachedCandidates(
        this Searcher searcher,
        RuntimeIndex index,
        FzfPattern pattern,
        int keep,
        FzfTopN matches,
        CancellationToken token,
        string cacheTerm,
        int[] candidates)
    {
        if (!pattern.TryGetSimpleFuzzyTerm(out var term) || term.Text.Length == 0)
            return false;

        var chunkCount = (candidates.Length + Helpers.NameSearchChunkSize - 1) / Helpers.NameSearchChunkSize;
        var chunkResults = new FzfTopN[chunkCount];
        var chunkCandidates = new List<int>[chunkCount];

        var maxDop = searcher.MaxDegreeOfParallelism > 0
            ? searcher.MaxDegreeOfParallelism
            : Math.Clamp(Environment.ProcessorCount, 2, 8);

        Parallel.For(
            0,
            chunkCount,
            new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = maxDop
            },
            () => new MatcherWorkerContext(keep),
            (chunk, _, worker) =>
            {
                token.ThrowIfCancellationRequested();
                worker.Reset();
                var start = chunk * Helpers.NameSearchChunkSize;
                var end = Math.Min(start + Helpers.NameSearchChunkSize, candidates.Length);

                for (var candidateIndex = start; candidateIndex < end; candidateIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    var i = candidates[candidateIndex];
                    if ((uint)i >= (uint)index.Count || index.IsDeleted(i))
                        continue;

                    var name = index.GetName(i);
                    if (string.IsNullOrEmpty(name))
                        continue;

                    var match = FzfAlgorithm.FuzzyMatchV1(name, term.Text, term.CaseSensitive, FzfScoringScheme.Default);
                    if (!match.IsMatch)
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
                                var aliasMatch = FzfAlgorithm.FuzzyMatchV1(alias, term.Text, term.CaseSensitive, FzfScoringScheme.Default);
                                if (aliasMatch.IsMatch)
                                {
                                    var span = aliasMatch.End - aliasMatch.Start;
                                    var patternLen = term.Text.Length;
                                    if (span > Math.Max(patternLen * 3, 20) || aliasMatch.Score < patternLen * 5)
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

                    worker.Candidates.Add(i);
                    worker.Matches.Add(FzfResultRank.ForDefaultScheme(i, name, Helpers.ToPatternResult(match)));
                }

                chunkResults[chunk] = worker.DetachMatches();
                chunkCandidates[chunk] = worker.DetachCandidates();
                return worker;
            },
            _ => { });

        var nextCandidates = new List<int>(Math.Min(candidates.Length, 16_384));
        for (var i = 0; i < chunkResults.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (chunkResults[i] != null)
                matches.AddRange(chunkResults[i].Finish(keep));
            if (chunkCandidates[i] != null)
                nextCandidates.AddRange(chunkCandidates[i]);
        }

        searcher.CacheManager.StoreCandidateCache(index, cacheTerm, nextCandidates);
        return true;
    }

    internal static void SearchCachedCandidatesParallel(
        this Searcher searcher,
        RuntimeIndex index,
        FzfPattern pattern,
        int keep,
        FzfTopN matches,
        CancellationToken token,
        string cacheTerm,
        int[] candidates)
    {
        var chunkCount = (candidates.Length + Helpers.NameSearchChunkSize - 1) / Helpers.NameSearchChunkSize;
        var chunkResults = new FzfTopN[chunkCount];
        var chunkCandidates = new List<int>[chunkCount];

        var maxDop = searcher.MaxDegreeOfParallelism > 0
            ? searcher.MaxDegreeOfParallelism
            : Math.Clamp(Environment.ProcessorCount, 2, 8);

        Parallel.For(
            0,
            chunkCount,
            new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = maxDop
            },
            () => new MatcherWorkerContext(keep),
            (chunk, _, worker) =>
            {
                token.ThrowIfCancellationRequested();
                worker.Reset();
                var start = chunk * Helpers.NameSearchChunkSize;
                var end = Math.Min(start + Helpers.NameSearchChunkSize, candidates.Length);

                for (var candidateIndex = start; candidateIndex < end; candidateIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    var i = candidates[candidateIndex];
                    if ((uint)i >= (uint)index.Count || index.IsDeleted(i))
                        continue;

                    var name = index.GetName(i);
                    if (string.IsNullOrEmpty(name))
                        continue;

                    if (!pattern.TryMatch(name, out var match, FzfScoringScheme.Default, worker.Slab))
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
                                if (pattern.TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default, worker.Slab))
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

                    worker.Candidates.Add(i);
                    worker.Matches.Add(FzfResultRank.ForDefaultScheme(i, name, match));
                }

                chunkResults[chunk] = worker.DetachMatches();
                chunkCandidates[chunk] = worker.DetachCandidates();
                return worker;
            },
            _ => { });

        var nextCandidates = new List<int>(Math.Min(candidates.Length, 16_384));
        for (var i = 0; i < chunkResults.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (chunkResults[i] != null)
                matches.AddRange(chunkResults[i].Finish(keep));
            if (chunkCandidates[i] != null)
                nextCandidates.AddRange(chunkCandidates[i]);
        }

        searcher.CacheManager.StoreCandidateCache(index, cacheTerm, nextCandidates);
    }
}
