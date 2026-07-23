using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using SwiftList.Core.SearchIndex;
using SwiftList.Core.SearchIndex.Fzf;

using SwiftList.Core.IndexV2.Persistence;
namespace SwiftList.Core.IndexV2.Search;

internal readonly record struct UniqueMatch(int Uid, FzfPatternResult Match, ulong SortKey);

// Phase A of name search: match every UNIQUE name in the snapshot against the pattern. Pure-ASCII
// names (baked bit, Snapshot.IsUniqueAscii) match on their raw UTF-8 bytes with zero decode
// (FzfBytePattern); the rest decode once into the worker's reusable scratch and match as spans -- no
// string is materialized per candidate, and the rank sort key is computed per-unique from the hot
// span (fanned out per row by NameSearch, since EntryIndex isn't packed into the key). The charmask
// prefilter covers multi-term OR sets (per-set "any term's mask covered") and its scan is
// AVX2-vectorized. Workers (slab + scratches + hit list) pool across searches. Delta rows (renamed/
// added, not in the unique table) are matched separately -- see SearchMatcherRow / NameSearch.
internal static class SearchMatcher
{
    internal const int ChunkSize = 8192;

    internal sealed class Worker
    {
        public readonly FzfSlab Slab = new();
        public readonly FzfByteBuffers ByteBuffers = new();
        public readonly List<UniqueMatch> Hits = new();
        public char[] Scratch = new char[256];
        public char[] AliasScratch = new char[256];
    }

    internal sealed class QueryContext
    {
        public required FzfPattern Pattern;
        public required FzfBytePattern BytePattern;
        public required ulong RequiredMask;   // union of single-term sets' masks: candidate must contain all
        public required ulong[][] OrSetMasks; // per multi-term set: candidate must cover at least one term
        public required bool CanFilter;
        public required int QueryLen;
        public required MixedTerm? MixedTerm; // non-null only for a bare single term mixing an alias provider's own two alphabets
    }

    private static readonly ConcurrentBag<Worker> WorkerPool = new();

    internal static Worker RentWorker() => WorkerPool.TryTake(out var pooled) ? pooled : new Worker();

    internal static void ReturnWorker(Worker worker)
    {
        worker.Hits.Clear();
        WorkerPool.Add(worker);
    }

    internal static QueryContext BuildContext(FzfPattern pattern)
    {
        ulong requiredMask = 0;
        List<ulong[]>? orSets = null;
        foreach (var set in pattern.TermSets)
        {
            // Any inverse term makes its whole set unfilterable (absence can't be mask-tested).
            var filterable = true;
            foreach (var term in set.Terms)
                filterable &= !term.Inverse;
            if (!filterable)
                continue;

            if (set.Terms.Length == 1)
            {
                requiredMask |= FzfAlgorithm.GetCharMask(set.Terms[0].Text);
            }
            else
            {
                var masks = new ulong[set.Terms.Length];
                for (var t = 0; t < set.Terms.Length; t++)
                    masks[t] = FzfAlgorithm.GetCharMask(set.Terms[t].Text);
                (orSets ??= new List<ulong[]>()).Add(masks);
            }
        }

        return new QueryContext
        {
            Pattern = pattern,
            BytePattern = FzfBytePattern.From(pattern),
            RequiredMask = requiredMask,
            OrSetMasks = orSets?.ToArray() ?? Array.Empty<ulong[]>(),
            CanFilter = requiredMask != 0 || orSets != null,
            QueryLen = pattern.GetTotalTermLength(),
            MixedTerm = MixedQueryMatcher.TrySegmentPattern(pattern),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool PassesOrSets(ulong candidateMask, ulong[][] orSets)
    {
        foreach (var masks in orSets)
        {
            var any = false;
            foreach (var m in masks)
            {
                if ((candidateMask & m) == m)
                {
                    any = true;
                    break;
                }
            }
            if (!any)
                return false;
        }
        return true;
    }

    // Pooled result lists: a broad single-char query's hit list reaches hundreds of thousands of
    // entries on a large drive, so reallocating it per keystroke was the last per-search allocation
    // of any size. Callers rent, consume, and return.
    private static readonly ConcurrentBag<List<UniqueMatch>> HitListPool = new();

    internal static List<UniqueMatch> RentHitList() => HitListPool.TryTake(out var pooled) ? pooled : new List<UniqueMatch>();

    internal static void ReturnHitList(List<UniqueMatch> list)
    {
        list.Clear();
        HitListPool.Add(list);
    }

    internal static void MatchUniques(Snapshot snapshot, FzfPattern pattern, List<UniqueMatch> merged, CancellationToken token = default)
    {
        var ctx = BuildContext(pattern);
        merged.Clear();
        var mergeLock = new object();
        var chunkCount = (snapshot.UniqueCount + ChunkSize - 1) / ChunkSize;

        // A broad/low-selectivity query (e.g. a single character) can touch a large fraction of the
        // whole unique-name table before this returns -- during normal rapid typing, every keystroke's
        // scan used to run to completion regardless of whether a newer keystroke had already superseded
        // it, piling up CPU contention across several abandoned scans at once. The CancellationToken
        // here lets a superseded scan abort between chunks instead of always running to the end.
        Parallel.For(
            0,
            Math.Max(chunkCount, 1),
            new ParallelOptions { CancellationToken = token },
            RentWorker,
            (chunk, _, worker) =>
            {
                var start = chunk * ChunkSize;
                var end = Math.Min(start + ChunkSize, snapshot.UniqueCount);
                var masks = snapshot.UniqueMasks;

                if (ctx.CanFilter && ctx.RequiredMask != 0 && Avx2.IsSupported && end - start >= 8)
                {
                    // Vectorized prefilter: 4 masks per iteration; only lanes whose mask covers the
                    // whole required set fall through to the scalar per-candidate work.
                    ref var m0 = ref MemoryMarshal.GetReference(masks);
                    var required = Vector256.Create(ctx.RequiredMask);
                    var i = start;
                    for (; i + 4 <= end; i += 4)
                    {
                        var v = Vector256.LoadUnsafe(ref Unsafe.Add(ref m0, i));
                        var bits = Vector256.Equals(Vector256.BitwiseAnd(v, required), required).ExtractMostSignificantBits();
                        if (bits == 0)
                            continue;
                        for (var lane = 0; lane < 4; lane++)
                        {
                            if ((bits & (1u << lane)) != 0 && PassesOrSets(masks[i + lane], ctx.OrSetMasks))
                                MatchOne(snapshot, ctx, i + lane, worker);
                        }
                    }
                    for (; i < end; i++)
                    {
                        if ((masks[i] & ctx.RequiredMask) == ctx.RequiredMask && PassesOrSets(masks[i], ctx.OrSetMasks))
                            MatchOne(snapshot, ctx, i, worker);
                    }
                }
                else
                {
                    for (var uid = start; uid < end; uid++)
                    {
                        if (ctx.CanFilter && ((masks[uid] & ctx.RequiredMask) != ctx.RequiredMask || !PassesOrSets(masks[uid], ctx.OrSetMasks)))
                            continue;
                        MatchOne(snapshot, ctx, uid, worker);
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
                ReturnWorker(worker);
            });
    }

    private static void MatchOne(Snapshot snapshot, QueryContext ctx, int uid, Worker worker)
    {
        var utf8 = snapshot.UniqueNameUtf8(uid);
        if (utf8.Length == 0)
            return;

        // Pure-ASCII name: bytes ARE the chars (same values, same offsets) -- match with zero decode.
        if (snapshot.IsUniqueAscii(uid))
        {
            if (ctx.BytePattern.TryMatch(utf8, out var byteMatch, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers))
            {
                worker.Hits.Add(new UniqueMatch(uid, byteMatch, FzfBytePattern.ForDefaultScheme(uid, utf8, byteMatch).SortKey));
                return;
            }
            if (snapshot.HasAliases(uid) && TryMatchAliases(snapshot, ctx, uid, worker, out var aliasBest))
                worker.Hits.Add(new UniqueMatch(uid, aliasBest, FzfBytePattern.ForDefaultScheme(uid, utf8, aliasBest).SortKey));
            return;
        }

        if (worker.Scratch.Length < utf8.Length)
            worker.Scratch = new char[Math.Max(utf8.Length, worker.Scratch.Length * 2)];
        var written = Encoding.UTF8.GetChars(utf8, worker.Scratch);
        var name = worker.Scratch.AsSpan(0, written);

        if (ctx.Pattern.TryMatch(name, out var match, FzfScoringScheme.Default, worker.Slab))
        {
            worker.Hits.Add(new UniqueMatch(uid, match, FzfResultRank.ForDefaultScheme(uid, name, match).SortKey));
        }
        else if (snapshot.HasAliases(uid) && TryMatchAliases(snapshot, ctx, uid, worker, out var best))
        {
            worker.Hits.Add(new UniqueMatch(uid, best, FzfResultRank.ForDefaultScheme(uid, name, best).SortKey));
        }
        else if (ctx.MixedTerm != null && snapshot.HasAliases(uid) && TryMatchMixed(snapshot, ctx, uid, name, out var mixedBest))
        {
            worker.Hits.Add(new UniqueMatch(uid, mixedBest, FzfResultRank.ForDefaultScheme(uid, name, mixedBest).SortKey));
        }
    }

    // Last-resort tier for a query mixing an alias provider's own two alphabets: only
    // the baked aliases belonging to that exact provider are worth trying, since MapAliasToSourceIndices
    // (needed to align the alias-syntax run back onto `name`) is only meaningful for the provider that
    // produced the alias. Decodes each candidate alias to UTF-16 -- acceptable here since this only runs
    // for the rare candidates that already failed both the literal-name and whole-query-alias tiers.
    private static bool TryMatchMixed(Snapshot snapshot, QueryContext ctx, int uid, ReadOnlySpan<char> name, out FzfPatternResult best)
    {
        best = default;
        var mixedTerm = ctx.MixedTerm!; // TrySegmentPattern already excluded a disabled provider from consideration
        var matched = false;
        var (start, end) = snapshot.AliasEntryRange(uid);
        string? nameStr = null;
        for (var e = start; e < end; e++)
        {
            if (snapshot.AliasProviderId(e) != mixedTerm.ProviderId)
                continue;

            var aliasUtf8 = snapshot.AliasUtf8(e);
            if (aliasUtf8.Length == 0)
                continue;

            nameStr ??= name.ToString();
            var aliasStr = Encoding.UTF8.GetString(aliasUtf8);
            foreach (var segment in aliasStr.Split('|'))
            {
                if (segment.Length == 0)
                    continue;
                if (!MixedQueryMatcher.TryMatch(mixedTerm, name, nameStr, segment, out var mm))
                    continue;

                var candidate = new FzfPatternResult(mm.Score, mm.MinBegin, mm.MaxEnd, mm.MaxEnd, mm.ValidOffsetFound);
                if (!matched || candidate.Score > best.Score)
                {
                    matched = true;
                    best = candidate;
                }
            }
        }
        return matched;
    }

    // Zero-copy alias fallback: each baked alias is matched from its raw UTF-8 (byte path for ASCII
    // aliases -- the common case, pinyin -- else decoded into the alias scratch), honoring
    // SearchContext.DisabledAliasIds and the IsAcceptableAliasMatch quality gate.
    internal static bool TryMatchAliases(Snapshot snapshot, QueryContext ctx, int uid, Worker worker, out FzfPatternResult best)
    {
        best = default;
        var matched = false;
        var disabledIds = SearchContext.DisabledAliasIds;
        var (start, end) = snapshot.AliasEntryRange(uid);
        for (var e = start; e < end; e++)
        {
            if (disabledIds != null && disabledIds.Contains(snapshot.AliasProviderId(e)))
                continue;

            var aliasUtf8 = snapshot.AliasUtf8(e);
            if (aliasUtf8.Length == 0)
                continue;

            FzfPatternResult aliasMatch;
            bool hit;
            var decodedLength = -1; // -1: not decoded to chars yet (the ASCII/byte fast path below skips it)
            if (Ascii.IsValid(aliasUtf8))
            {
                hit = ctx.BytePattern.TryMatchSegmented(aliasUtf8, out aliasMatch, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers);
            }
            else
            {
                if (worker.AliasScratch.Length < aliasUtf8.Length)
                    worker.AliasScratch = new char[Math.Max(aliasUtf8.Length, worker.AliasScratch.Length * 2)];
                decodedLength = Encoding.UTF8.GetChars(aliasUtf8, worker.AliasScratch);
                hit = ctx.Pattern.TryMatch(worker.AliasScratch.AsSpan(0, decodedLength), out aliasMatch, FzfScoringScheme.Default, worker.Slab);
            }

            if (hit)
            {
                var acceptable = ctx.Pattern.IsAcceptableAliasMatch(aliasMatch, ctx.QueryLen);
                if (!acceptable)
                {
                    // The multi-term "every term individually tight" fallback (see FzfPattern's own
                    // comment on IsAcceptableAliasMatch) needs the alias as chars -- the ASCII/byte fast
                    // path above deliberately never decodes it, since the common case doesn't need to.
                    // Only pay that decode cost here, in this already-rare tail (existing check failed).
                    if (decodedLength < 0)
                    {
                        if (worker.AliasScratch.Length < aliasUtf8.Length)
                            worker.AliasScratch = new char[Math.Max(aliasUtf8.Length, worker.AliasScratch.Length * 2)];
                        decodedLength = Encoding.UTF8.GetChars(aliasUtf8, worker.AliasScratch);
                    }
                    acceptable = ctx.Pattern.IsAcceptableAliasMatch(aliasMatch, ctx.QueryLen, worker.AliasScratch.AsSpan(0, decodedLength), FzfScoringScheme.Default, worker.Slab);
                }

                if (acceptable)
                {
                    var weighted = ctx.Pattern.WeightAliasMatch(aliasMatch, ctx.QueryLen);
                    if (!matched || weighted.Score > best.Score)
                    {
                        matched = true;
                        best = weighted;
                    }
                }
            }
        }
        return matched;
    }
}
