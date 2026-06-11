using System.Diagnostics;
using SwiftList.Core.SearchIndex.Fzf;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.SearchIndex.RecordSearch;

internal static class FastInitialExtensions
{
    public static bool TrySearchFastInitialCandidates(
        this Searcher searcher,
        RuntimeIndex index,
        FzfPattern pattern,
        int keep,
        FzfTopN matches,
        CancellationToken token,
        string? directoryFilterLower,
        UInt128? directoryRootId,
        List<int>? matchedCandidates,
        out bool completed)
    {
        completed = true;
        if (directoryFilterLower != null ||
            directoryRootId != null ||
            matchedCandidates == null ||
            !pattern.TryGetSimpleFuzzyTerm(out var term) ||
            term.Text.Length == 0)
        {
            return false;
        }

        var roughKeep = Math.Max(keep * 16, Helpers.FastRerankMinimum);
        index.GetNameCandidateStorage(term.Text, out var bucket, out var delta);
        var candidateCount = (bucket?.Length ?? 0) + (delta?.Count ?? 0);

        if (candidateCount == 0)
            return true;

        var budget = new FastSearchBudget(Helpers.FastInitialBudgetMilliseconds);
        if (candidateCount >= Helpers.ParallelNameSearchThreshold && bucket != null)
            completed = FastInitialCandidatesParallel(index, term, roughKeep, bucket, delta, matchedCandidates, matches, token, budget);
        else
            completed = FastInitialCandidatesSerial(index, term, roughKeep, matchedCandidates, matches, bucket, delta, token, budget);

        return true;
    }

    private static bool FastInitialCandidatesSerial(
        RuntimeIndex index,
        FzfTerm term,
        int roughKeep,
        List<int> matchedCandidates,
        FzfTopN matches,
        int[]? bucket,
        List<int>? delta,
        CancellationToken token,
        FastSearchBudget budget)
    {
        var rough = new FzfTopN(roughKeep);
        var queryMask = FzfAlgorithm.GetCharMask(term.Text);

        if (bucket != null)
        {
            foreach (var i in bucket)
            {
                if (budget.IsExpired)
                {
                    matches.AddRange(rough.Finish(roughKeep));
                    return false;
                }

                AddFastInitialCandidate(index, term, queryMask, matchedCandidates, rough, token, i);
            }
        }

        if (delta != null)
        {
            foreach (var i in delta)
            {
                if (budget.IsExpired)
                {
                    matches.AddRange(rough.Finish(roughKeep));
                    return false;
                }

                AddFastInitialCandidate(index, term, queryMask, matchedCandidates, rough, token, i);
            }
        }

        matches.AddRange(rough.Finish(roughKeep));
        return true;
    }

    private static bool FastInitialCandidatesParallel(
        RuntimeIndex index,
        FzfTerm term,
        int roughKeep,
        int[] bucket,
        List<int>? delta,
        List<int> matchedCandidates,
        FzfTopN matches,
        CancellationToken token,
        FastSearchBudget budget)
    {
        var count = bucket.Length;
        var queryMask = FzfAlgorithm.GetCharMask(term.Text);
        var chunkCount = (count + Helpers.NameSearchChunkSize - 1) / Helpers.NameSearchChunkSize;
        var chunkResults = new FzfTopN[chunkCount];
        var chunkCandidates = new List<int>[chunkCount];
        var completed = 1;

        Parallel.For(
            0,
            chunkCount,
            new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8)
            },
            () => new MatcherWorkerContext(roughKeep),
            (chunk, _, worker) =>
            {
                token.ThrowIfCancellationRequested();
                worker.Reset();
                var start = chunk * Helpers.NameSearchChunkSize;
                var end = Math.Min(start + Helpers.NameSearchChunkSize, count);

                for (var candidateIndex = start; candidateIndex < end; candidateIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    if ((candidateIndex & 0xFF) == 0 && budget.IsExpired)
                    {
                        Interlocked.Exchange(ref completed, 0);
                        break;
                    }

                    var i = bucket[candidateIndex];
                    if (index.IsDeleted(i))
                        continue;

                    if ((index.CharMasks[i] & queryMask) != queryMask)
                        continue;

                    var name = index.GetName(i);
                    if (string.IsNullOrEmpty(name))
                        continue;

                    var match = FzfAlgorithm.FuzzyMatchV1(name, term.Text, term.CaseSensitive, FzfScoringScheme.Default);
                    if (!match.IsMatch)
                    {
                        if (index.TryGetAliases(i, out var aliases))
                        {
                            var aliasMatched = false;
                            foreach (var alias in aliases)
                            {
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

        var rough = new FzfTopN(roughKeep);
        for (var i = 0; i < chunkResults.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (chunkResults[i] != null)
                rough.AddRange(chunkResults[i].Finish(roughKeep));
            if (chunkCandidates[i] != null)
                matchedCandidates.AddRange(chunkCandidates[i]);
        }

        if (delta != null)
        {
            foreach (var i in delta)
            {
                if (budget.IsExpired)
                {
                    completed = 0;
                    break;
                }

                AddFastInitialCandidate(index, term, queryMask, matchedCandidates, rough, token, i);
            }
        }

        matches.AddRange(rough.Finish(roughKeep));
        return completed != 0;
    }

    private static void AddFastInitialCandidate(
        RuntimeIndex index,
        FzfTerm term,
        ulong queryMask,
        List<int> matchedCandidates,
        FzfTopN rough,
        CancellationToken token,
        int i)
    {
        token.ThrowIfCancellationRequested();
        if ((uint)i >= (uint)index.Count || index.IsDeleted(i))
            return;

        if ((index.CharMasks[i] & queryMask) != queryMask)
            return;

        var name = index.GetName(i);
        if (string.IsNullOrEmpty(name))
            return;

        var match = FzfAlgorithm.FuzzyMatchV1(name, term.Text, term.CaseSensitive, FzfScoringScheme.Default);
        if (!match.IsMatch)
        {
            if (index.TryGetAliases(i, out var aliases))
            {
                var aliasMatched = false;
                foreach (var alias in aliases)
                {
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
                    return;
            }
            else
            {
                return;
            }
        }

        matchedCandidates.Add(i);
        rough.Add(FzfResultRank.ForDefaultScheme(i, name, Helpers.ToPatternResult(match)));
    }

    private sealed class FastSearchBudget
    {
        private readonly long _deadline;

        public FastSearchBudget(int milliseconds) => _deadline = Stopwatch.GetTimestamp() + milliseconds * Stopwatch.Frequency / 1000;

        public bool IsExpired => Stopwatch.GetTimestamp() >= _deadline;
    }
}
