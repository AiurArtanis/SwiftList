using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using SwiftList.Core.SearchIndex.Fzf;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.SearchIndex.RecordSearch;

internal static class ParallelStreamingSearchExtensions
{
    internal static void SearchNameCandidatesParallelStreaming(
        RuntimeIndex index,
        FzfPattern pattern,
        int keep,
        FzfTopN matches,
        Action<int, string, FzfPatternResult> streamCallback,
        CancellationToken token,
        List<int>? matchedCandidates,
        int maxDegreeOfParallelism)
    {
        var count = index.Count;
        var queryMask = pattern.GetQueryMask(out var canFilter);
        var chunkCount = (count + Helpers.NameSearchChunkSize - 1) / Helpers.NameSearchChunkSize;
        var chunkResults = new FzfTopN[chunkCount];
        var chunkCandidates = matchedCandidates == null ? null : new List<int>[chunkCount];

        var maxDop = maxDegreeOfParallelism > 0
            ? maxDegreeOfParallelism
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

                                if (StreamingSearchExtensions.MatchCandidate(index, idx, pattern, worker.Slab, out var name, out var match))
                                {
                                    localCandidates?.Add(idx);
                                    worker.Matches.Add(FzfResultRank.ForDefaultScheme(idx, name, match));
                                    streamCallback(idx, name, match);
                                }
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

                    if (StreamingSearchExtensions.MatchCandidate(index, i, pattern, worker.Slab, out var name, out var match))
                    {
                        localCandidates?.Add(i);
                        worker.Matches.Add(FzfResultRank.ForDefaultScheme(i, name, match));
                        streamCallback(i, name, match);
                    }
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
}
