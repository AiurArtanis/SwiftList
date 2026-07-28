using System.Text;
using SwiftList.Core.SearchIndex.Fzf;

using SwiftList.Core.IndexV2.Delta;

using SwiftList.Core.IndexV2.Persistence;
namespace SwiftList.Core.IndexV2.Search;

// Order-free "a term may be satisfied by an ancestor folder instead of the file name" pass, run by
// NameSearch only when plain name matching emitted nothing at all. That trigger is what keeps this
// cheap: an ordinary query never reaches it, and because there are no real name hits to interleave
// with, the ranking can reuse the matched term's own sort key untouched -- no sort-key surgery and no
// risk of pushing a genuine name match down.
//
// Distinct from PathSearch, which models a POSITIONAL "dir\subdir\file" query: there the query's
// segments must appear in ancestors in that same order. Here the terms carry no position at all, so
// each ancestor is offered every still-unsatisfied term (fzf terms are already order-free against a
// name; this extends the same property across the path).
//
// Deliberate v1 restriction: at least one term must match the FILE NAME. Without it the candidate set
// becomes "every row under any folder matching any term", which one common folder name turns
// into most of a drive -- and that set cannot be enumerated from phase A's per-term unique hits, which
// is exactly what keeps the row walk here bounded to the same order of work as an ordinary search.
internal static class PathTermFallback
{
    // The satisfied-term set is carried as a bitmask, so a query with more terms than bits simply
    // opts out rather than silently matching on a truncated set.
    private const int MaxTerms = 16;

    public static void SearchStreaming(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, int limit,
        Action<SearchResult> onResult, CancellationToken token, string? directoryFilterLower)
    {
        var termCount = pattern.TermSets.Length;
        // One term has nowhere to split: the "at least one term matches the name" rule would make this
        // identical to the name search that just came up empty.
        if (termCount < 2 || termCount > MaxTerms)
            return;

        var directoryContext = NameSearch.ResolveDirectoryContext(snapshot, delta, directoryFilterLower);
        if (directoryContext.Excluded)
            return;

        var termPatterns = new FzfPattern[termCount];
        var termBytePatterns = new FzfBytePattern[termCount];
        for (var i = 0; i < termCount; i++)
        {
            termPatterns[i] = FzfPattern.ForTermSet(pattern, i);
            termBytePatterns[i] = FzfBytePattern.From(termPatterns[i]);
        }

        // Phase A once per term instead of once per query: which unique names satisfy term i, and with
        // what sort key (kept so an emitted row can rank by the term that actually hit its name).
        var nameMasks = new Dictionary<int, int>();
        var nameRanks = new Dictionary<int, (int Score, ulong SortKey)>();
        var fullMask = (1 << termCount) - 1;
        for (var i = 0; i < termCount; i++)
        {
            token.ThrowIfCancellationRequested();
            var hits = SearchMatcher.RentHitList();
            SearchMatcher.MatchUniques(snapshot, termPatterns[i], hits, token);
            foreach (var m in hits)
            {
                nameMasks[m.Uid] = nameMasks.TryGetValue(m.Uid, out var existing) ? existing | (1 << i) : 1 << i;
                // Keep the strongest term hit as the row's ranking basis.
                if (!nameRanks.TryGetValue(m.Uid, out var best) || m.Match.Score > best.Score)
                    nameRanks[m.Uid] = (m.Match.Score, m.SortKey);
            }
            SearchMatcher.ReturnHitList(hits);
        }

        if (nameMasks.Count == 0)
            return;

        var ancestorMemo = new Dictionary<int, int>();
        var membership = directoryContext.FilterLower != null ? new Dictionary<int, bool>() : null;
        var worker = SearchMatcher.RentWorker();
        var keep = Math.Max(limit * 8, 64);
        var topN = new FzfTopN(keep);
        try
        {
            foreach (var (uid, nameMask) in nameMasks)
            {
                token.ThrowIfCancellationRequested();
                // A name satisfying every term on its own would already have been emitted by the name
                // search that came up empty, so it cannot occur here -- but skipping it costs nothing
                // and keeps this pass from ever double-reporting if the trigger is ever loosened.
                if (nameMask == fullMask)
                    continue;

                var rank = nameRanks[uid];
                foreach (var row in snapshot.RowsForUid(uid))
                {
                    if (snapshot.IsDeleted(row) || delta.IsSuperseded(row))
                        continue;
                    if (membership != null && !NameSearch.RowMatchesFilter(snapshot, delta, row, delta.GetFullPath(row), directoryContext, membership))
                        continue;

                    var parent = snapshot.ParentIndexes[row];
                    if (parent == row || parent < 0)
                        continue;
                    if ((nameMask | AncestorMask(snapshot, delta, parent, termPatterns, termBytePatterns, worker, ancestorMemo, fullMask)) != fullMask)
                        continue;

                    topN.Add(new FzfRank(row, rank.Score, rank.SortKey));
                }
            }
        }
        finally
        {
            SearchMatcher.ReturnWorker(worker);
        }

        var emitted = 0;
        var seen = new HashSet<int>();
        foreach (var rank in topN.Finish(keep))
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(rank.EntryIndex))
                continue;
            onResult(ResultBuilder.ToResult(snapshot, delta, rank));
            if (++emitted >= limit)
                break;
        }
    }

    // Which terms this parent's ancestor chain satisfies. Memoized on the parent row alone -- the
    // verdict depends only on the chain, never on the file sitting in it, so every file in a folder
    // (and every folder under an already-walked one) reuses the same answer. Mirrors PathGate's walk:
    // stop at a negative or self parent, skip empty names, then offer the source root's own segments.
    private static int AncestorMask(Snapshot snapshot, DeltaOverlay delta, int parentRow,
        FzfPattern[] termPatterns, FzfBytePattern[] termBytePatterns, SearchMatcher.Worker worker,
        Dictionary<int, int> memo, int fullMask)
    {
        if (memo.TryGetValue(parentRow, out var cached))
            return cached;

        var mask = 0;
        var current = parentRow;
        for (var depth = 0; depth < 512 && current >= 0 && mask != fullMask; depth++)
        {
            if (delta.IsSuperseded(current))
            {
                // A renamed/overridden ancestor's live name only exists in delta state, so fall back to
                // the built path string for the whole chain -- the same escape hatch PathGate takes.
                mask |= MaskFromPath(delta.GetFullPath(parentRow), termPatterns, worker);
                break;
            }

            var uid = (int)snapshot.NameIds[current];
            var nameUtf8 = snapshot.UniqueNameUtf8(uid);
            if (nameUtf8.Length > 0)
                mask |= MaskForSegment(snapshot, uid, nameUtf8, termPatterns, termBytePatterns, worker, mask, fullMask);

            var parent = snapshot.ParentIndexes[current];
            if (parent == current)
                break;
            current = parent;
        }

        if (mask != fullMask)
            mask |= MaskFromSegments(snapshot.SourceRoot.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries), termPatterns, worker, mask);

        memo[parentRow] = mask;
        return mask;
    }

    private static int MaskForSegment(Snapshot snapshot, int uid, ReadOnlySpan<byte> nameUtf8,
        FzfPattern[] termPatterns, FzfBytePattern[] termBytePatterns, SearchMatcher.Worker worker, int already, int fullMask)
    {
        var mask = 0;
        var ascii = snapshot.IsUniqueAscii(uid);
        var written = 0;
        if (!ascii)
        {
            if (worker.Scratch.Length < nameUtf8.Length)
                worker.Scratch = new char[Math.Max(nameUtf8.Length, worker.Scratch.Length * 2)];
            written = Encoding.UTF8.GetChars(nameUtf8, worker.Scratch);
        }

        for (var i = 0; i < termPatterns.Length; i++)
        {
            var bit = 1 << i;
            if ((already & bit) != 0)
                continue; // already satisfied deeper in the chain
            var hit = ascii
                ? termBytePatterns[i].TryMatch(nameUtf8, out _, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers)
                : termPatterns[i].TryMatch(worker.Scratch.AsSpan(0, written), out _, FzfScoringScheme.Default, worker.Slab);
            if (hit)
                mask |= bit;
        }

        var unresolved = fullMask & ~(already | mask);
        if (unresolved != 0)
            mask |= MaskFromAliases(snapshot, uid, termPatterns, termBytePatterns, worker, unresolved);
        return mask;
    }

    // Baked-alias fallback, mirroring PathGate's: without it a folder named in a non-Latin script can
    // only ever be reached by typing its literal name, which defeats the whole point for a CJK library
    // ("dcj" has to reach a folder whose pinyin initials are d-c-j). Aliases are walked once and each
    // one offered every still-unresolved term, so a segment with many readings decodes at most once per
    // alias rather than once per term. Ungated and first-match-wins, matching PathGate.
    private static int MaskFromAliases(Snapshot snapshot, int uid, FzfPattern[] termPatterns,
        FzfBytePattern[] termBytePatterns, SearchMatcher.Worker worker, int unresolved)
    {
        var mask = 0;
        var disabledIds = SearchContext.DisabledAliasIds;
        var (start, end) = snapshot.AliasEntryRange(uid);
        for (var e = start; e < end && mask != unresolved; e++)
        {
            if (disabledIds != null && disabledIds.Contains(snapshot.AliasProviderId(e)))
                continue;
            var aliasUtf8 = snapshot.AliasUtf8(e);
            if (aliasUtf8.Length == 0)
                continue;

            var ascii = Ascii.IsValid(aliasUtf8);
            var written = 0;
            if (!ascii)
            {
                if (worker.AliasScratch.Length < aliasUtf8.Length)
                    worker.AliasScratch = new char[Math.Max(aliasUtf8.Length, worker.AliasScratch.Length * 2)];
                written = Encoding.UTF8.GetChars(aliasUtf8, worker.AliasScratch);
            }

            for (var i = 0; i < termPatterns.Length; i++)
            {
                var bit = 1 << i;
                if ((unresolved & bit) == 0 || (mask & bit) != 0)
                    continue;
                // TryMatchSegmented on the byte side: one alias string can hold several polyphonic
                // readings joined by '|', and a term must land inside a single reading.
                var hit = ascii
                    ? termBytePatterns[i].TryMatchSegmented(aliasUtf8, out _, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers)
                    : termPatterns[i].TryMatch(worker.AliasScratch.AsSpan(0, written), out _, FzfScoringScheme.Default, worker.Slab);
                if (hit)
                    mask |= bit;
            }
        }
        return mask;
    }

    private static int MaskFromPath(string path, FzfPattern[] termPatterns, SearchMatcher.Worker worker)
        => MaskFromSegments(path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries), termPatterns, worker, 0);

    private static int MaskFromSegments(string[] segments, FzfPattern[] termPatterns, SearchMatcher.Worker worker, int already)
    {
        var mask = 0;
        foreach (var segment in segments)
        {
            for (var i = 0; i < termPatterns.Length; i++)
            {
                var bit = 1 << i;
                if (((already | mask) & bit) != 0)
                    continue;
                if (termPatterns[i].TryMatch(segment, out _, FzfScoringScheme.Default, worker.Slab))
                    mask |= bit;
            }
        }
        return mask;
    }
}
