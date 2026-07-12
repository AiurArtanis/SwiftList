using SwiftList.Core.SearchIndex;
using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.IndexV2;

// Name-mode search: phase A matches unique names (SearchMatcher) + delta rows (renamed/added, matched
// individually since they aren't folded into the unique table until compaction); phase B fans each
// matched unique out through the uid->rows CSR and ranks everything with FzfTopN. Delta rows rank
// under their original row index (base overrides -- the old engine renames in place) or a synthetic
// index past Count in insertion order (added rows) -- FzfRank breaks ties by EntryIndex, so relative
// order stays equivalent to the old engine's append-and-rename-in-place behavior.
internal static class NameSearch
{
    private static readonly FzfPatternResult EmptyPatternMatch = new(0, int.MaxValue, int.MaxValue, 0, false);

    // The percentage*consecutiveness ranking weight (HighlightMask.ComputeWeight) is too expensive to
    // compute per-candidate in the hot scan (measured ~10us/candidate for a typical fuzzy multi-term
    // query -- dominated by the DP fuzzy-highlight fallback for scattered matches) when a broad query
    // can match tens of thousands of names. Instead, the scan keeps a WIDER unweighted top-N than what
    // gets displayed, and only that bounded headroom set gets refined with the real weight afterward --
    // cost becomes a small constant, independent of how many candidates the query actually matched.
    private const int RefinementHeadroomFactor = 5;
    private const int RefinementScanCap = 4000;

    public static void SearchStreaming(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, int limit,
        Action<SearchResult> onResult, CancellationToken token, string? directoryFilterLower)
    {
        if (!DriveAdmits(snapshot, pattern, out var matchAll))
            return;

        var directoryContext = ResolveDirectoryContext(snapshot, delta, directoryFilterLower);
        if (directoryContext.Excluded)
            return;

        var keep = Math.Max(limit * 8, 64);
        var scanKeep = matchAll || pattern.IsEmpty ? keep : Math.Min(keep * RefinementHeadroomFactor, RefinementScanCap);
        var topN = new FzfTopN(scanKeep);
        CollectRanks(snapshot, delta, pattern, matchAll, directoryContext, rank => topN.Add(rank), token);

        var ranks = topN.Finish(scanKeep);
        if (!matchAll && !pattern.IsEmpty)
            RefineWithWeight(snapshot, delta, pattern, ranks);

        var seen = new HashSet<int>();
        var emitted = 0;
        foreach (var rank in ranks)
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(rank.EntryIndex))
                continue;
            onResult(ResultBuilder.ToResult(snapshot, delta, rank));
            if (++emitted >= limit)
                break;
        }
    }

    // Bounded refinement: only ever runs over the scanKeep-sized headroom set above, never the full
    // matched set. Ranking-only (FzfResultRank.ApplyWeight never rejects), so this can't drop a result.
    private static void RefineWithWeight(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, List<FzfRank> ranks)
    {
        for (var i = 0; i < ranks.Count; i++)
        {
            var rank = ranks[i];
            var name = GetNameForEntry(snapshot, delta, rank.EntryIndex);
            if (name.Length == 0)
                continue;
            var weight = HighlightMask.ComputeWeight(name, pattern);
            ranks[i] = FzfResultRank.ApplyWeight(rank, weight);
        }
        FzfRankRadixSorter.Sort(ranks);
    }

    // Mirrors ResultBuilder.ToResult's entryIndex->name resolution (base row, possibly overridden, vs
    // an Added delta record past Snapshot.Count).
    private static string GetNameForEntry(Snapshot snapshot, DeltaOverlay delta, int entryIndex)
        => entryIndex >= snapshot.Count ? delta.Added[entryIndex - snapshot.Count].Name : delta.NameOf(entryIndex);

    // Mirrors Searcher's drive gate: a foreign-drive query returns nothing; a bare drive prefix with
    // no terms ("t:") matches everything (TryMatch trivially succeeds on an empty pattern).
    private static bool DriveAdmits(Snapshot snapshot, FzfPattern pattern, out bool matchAll)
    {
        matchAll = false;
        if (pattern.TargetDrive != null && !pattern.TargetDrive.Equals(snapshot.SourceKey, StringComparison.OrdinalIgnoreCase))
            return false;
        if (pattern.IsEmpty)
        {
            if (pattern.TargetDrive == null)
                return false;
            matchAll = true;
        }
        return true;
    }

    internal readonly record struct DirectoryContext(bool Excluded, int RootFilterRow, int AncestorRow, string? FilterLower);

    internal static DirectoryContext ResolveDirectoryContext(Snapshot snapshot, DeltaOverlay? delta, string? directoryFilterLower)
    {
        var sourceRootLower = snapshot.SourceRoot.ToLowerInvariant();
        if (directoryFilterLower != null && directoryFilterLower.Equals(sourceRootLower, StringComparison.Ordinal))
            directoryFilterLower = null;
        if (DirectoryFilterResolver.ExcludesSource(snapshot, directoryFilterLower))
            return new DirectoryContext(true, -1, -1, directoryFilterLower);
        if (directoryFilterLower == null)
            return new DirectoryContext(false, -1, -1, null);

        var rootFilterRow = -1;
        var ancestorRow = -1;
        if (DirectoryFilterResolver.TryResolve(snapshot, delta, directoryFilterLower, forceLastSegmentAsQuery: false, out var resolved, out var remainder))
        {
            if (remainder.Length == 0)
                rootFilterRow = resolved;
            else
                ancestorRow = resolved;
        }
        return new DirectoryContext(false, rootFilterRow, ancestorRow, directoryFilterLower);
    }

    // True when `row` (a base-snapshot row) satisfies the resolved directory filter.
    internal static bool RowMatchesFilter(Snapshot snapshot, DeltaOverlay? delta, int row, string path, DirectoryContext ctx, Dictionary<int, bool> membership)
    {
        if (ctx.FilterLower == null)
            return true;
        if (ctx.RootFilterRow >= 0)
            return DirectoryFilterResolver.IsUnderCached(snapshot, row, ctx.RootFilterRow, membership);
        if (ctx.AncestorRow >= 0 && !DirectoryFilterResolver.IsUnderCached(snapshot, row, ctx.AncestorRow, membership))
            return false;
        return path.StartsWith(ctx.FilterLower, StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectRanks(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, bool matchAll, DirectoryContext ctx, Action<FzfRank> add, CancellationToken token)
    {
        var membership = ctx.FilterLower != null ? new Dictionary<int, bool>() : null;

        if (matchAll)
        {
            // Unique-first like the pattern path below: the empty-pattern sort key depends only on the
            // name, so it's computed once per unique instead of materializing a string per row.
            // Superseded rows are skipped in the fanout, so no override name can be needed here.
            var worker = SearchMatcher.RentWorker();
            for (var uid = 0; uid < snapshot.UniqueCount; uid++)
            {
                if ((uid & 0xFFF) == 0)
                    token.ThrowIfCancellationRequested();
                var utf8 = snapshot.UniqueNameUtf8(uid);
                if (utf8.Length == 0)
                    continue;
                var sortKey = MatchAllSortKey(snapshot, uid, worker, utf8);
                foreach (var row in snapshot.RowsForUid(uid))
                {
                    if (snapshot.IsDeleted(row) || delta.IsSuperseded(row))
                        continue;
                    if (membership != null && !RowMatchesFilter(snapshot, delta, row, delta.GetFullPath(row), ctx, membership))
                        continue;
                    add(new FzfRank(row, 0, sortKey));
                }
            }
            SearchMatcher.ReturnWorker(worker);
        }
        else
        {
            var hits = SearchMatcher.RentHitList();
            SearchMatcher.MatchUniques(snapshot, pattern, hits, token);
            foreach (var m in hits)
            {
                foreach (var row in snapshot.RowsForUid(m.Uid))
                {
                    if (snapshot.IsDeleted(row) || delta.IsSuperseded(row))
                        continue;
                    if (membership != null && !RowMatchesFilter(snapshot, delta, row, delta.GetFullPath(row), ctx, membership))
                        continue;
                    // The per-unique sort key applies verbatim to every row of that unique --
                    // EntryIndex isn't packed into the key, so nothing is recomputed per row.
                    add(new FzfRank(row, m.Match.Score, m.SortKey));
                }
            }
            SearchMatcher.ReturnHitList(hits);
        }

        MatchDeltaRows(snapshot, delta, pattern, matchAll, ctx, add);
    }

    private static ulong MatchAllSortKey(Snapshot snapshot, int uid, SearchMatcher.Worker worker, ReadOnlySpan<byte> utf8)
    {
        if (snapshot.IsUniqueAscii(uid))
            return FzfBytePattern.ForDefaultScheme(0, utf8, EmptyPatternMatch).SortKey;
        if (worker.Scratch.Length < utf8.Length)
            worker.Scratch = new char[Math.Max(utf8.Length, worker.Scratch.Length * 2)];
        var written = System.Text.Encoding.UTF8.GetChars(utf8, worker.Scratch);
        return FzfResultRank.ForDefaultScheme(0, worker.Scratch.AsSpan(0, written), EmptyPatternMatch).SortKey;
    }

    // Delta churn is always small (live USN/watcher batches, not bulk scans), so both loops just check
    // the row's own full path against the filter prefix -- correct for renamed/moved/added rows alike,
    // unlike the row-index ancestor cache above (a snapshot-only optimization for the hot base-row path).
    private static void MatchDeltaRows(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, bool matchAll, DirectoryContext ctx, Action<FzfRank> add)
    {
        var slab = new FzfSlab();
        var queryLen = pattern.GetTotalTermLength();

        foreach (var (row, record) in delta.BaseOverrides)
        {
            if (record.Name.Length == 0)
                continue;
            var match = EmptyPatternMatch;
            if (!matchAll && !SearchMatcherRow.TryMatchNameOrAliases(pattern, record.Name, record.Aliases, record.ProviderIds, queryLen, slab, out match))
                continue;
            if (ctx.FilterLower != null && !delta.GetFullPath(row).StartsWith(ctx.FilterLower, StringComparison.OrdinalIgnoreCase))
                continue;
            add(FzfResultRank.ForDefaultScheme(row, record.Name, match));
        }
        for (var i = 0; i < delta.Added.Count; i++)
        {
            var record = delta.Added[i];
            if (record.Removed || record.Name.Length == 0)
                continue;
            var match = EmptyPatternMatch;
            if (!matchAll && !SearchMatcherRow.TryMatchNameOrAliases(pattern, record.Name, record.Aliases, record.ProviderIds, queryLen, slab, out match))
                continue;
            if (ctx.FilterLower != null && !delta.GetFullPath(record).StartsWith(ctx.FilterLower, StringComparison.OrdinalIgnoreCase))
                continue;
            add(FzfResultRank.ForDefaultScheme(snapshot.Count + i, record.Name, match));
        }
    }
}
