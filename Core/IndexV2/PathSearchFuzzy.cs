using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.IndexV2;

// Fuzzy path-mode matching, mirroring PathExtensions.SearchPath's two branches: a query with a
// directory segment matches the file part per row then verifies the parent path's segments
// right-to-left (with a per-segment alias fallback, ungated unlike name search's IsAcceptableAliasMatch
// gate -- matches the old engine's TryMatchSegmentWithAlias exactly); a bare query with no directory
// segment falls back to a full-tree fuzzy filename scan. Scans rows directly rather than unique-first:
// each row's directory context differs even for rows sharing a name, so per-row is unavoidable here
// (matches the old engine's own approach).
internal static class PathSearchFuzzy
{
    public static void SearchStreaming(Snapshot snapshot, DeltaOverlay delta, string pathQuery, int limit,
        Action<SearchResult> onResult, CancellationToken token, string? directoryFilterLower)
    {
        var keep = Math.Max(limit * 8, 64);
        var matches = new FzfTopN(keep);
        var slab = new FzfSlab();
        var aliasScratch = new List<(string Alias, byte ProviderId)>();

        var lastSep = pathQuery.LastIndexOf(Path.DirectorySeparatorChar);
        var dirQuery = lastSep >= 0 ? pathQuery.Substring(0, lastSep) : string.Empty;
        var fileQuery = lastSep >= 0 ? pathQuery.Substring(lastSep + 1) : pathQuery;

        if (!string.IsNullOrEmpty(dirQuery))
            SearchWithDirectory(snapshot, delta, dirQuery, fileQuery, matches, slab, aliasScratch, token, directoryFilterLower);
        else
            SearchFilenameOnly(snapshot, delta, pathQuery, matches, slab, aliasScratch, token, directoryFilterLower);

        var seen = new HashSet<int>();
        var emitted = 0;
        foreach (var rank in matches.Finish(keep))
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(rank.EntryIndex))
                continue;
            onResult(ResultBuilder.ToResult(snapshot, delta, rank));
            if (++emitted >= limit)
                break;
        }
    }

    private static void SearchWithDirectory(Snapshot snapshot, DeltaOverlay delta, string dirQuery, string fileQuery,
        FzfTopN matches, FzfSlab slab, List<(string Alias, byte ProviderId)> aliasScratch, CancellationToken token, string? directoryFilterLower)
    {
        var filePattern = !string.IsNullOrEmpty(fileQuery) ? FzfPattern.ParseText(fileQuery) : null;
        var fileQueryLen = filePattern?.GetTotalTermLength() ?? 0;
        var querySegments = dirQuery.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

        for (var row = 0; row < snapshot.Count; row++)
        {
            token.ThrowIfCancellationRequested();
            if (snapshot.IsDeleted(row) || delta.IsSuperseded(row))
                continue;

            FzfPatternResult fileMatch = default;
            string name;
            if (filePattern != null)
            {
                if (!SearchMatcher.MatchRow(snapshot, row, filePattern, fileQueryLen, slab, aliasScratch, out name, out fileMatch))
                    continue;
            }
            else
            {
                name = snapshot.GetName(row);
            }

            var parentIndex = snapshot.ParentIndexes[row];
            if (parentIndex < 0)
                continue;

            var parentPath = delta.GetFullPath(parentIndex);
            var dirScore = VerifyPathSegmentsMatch(parentPath, querySegments, slab);
            if (dirScore <= 0)
                continue;

            fileMatch = fileMatch with { Score = fileMatch.Score + dirScore };
            var path = delta.GetFullPath(row);
            if (directoryFilterLower != null && !path.StartsWith(directoryFilterLower, StringComparison.OrdinalIgnoreCase))
                continue;

            var rank = FzfResultRank.ForDefaultScheme(row, name, fileMatch);
            var relativeDepth = GetRelativeDepth(path, parentPath);
            var point3 = (ushort)((Math.Min(relativeDepth, 255) << 8) | (Math.Min(name.Length, 255) & 0xFF));
            var sortKey = rank.SortKey;
            sortKey &= ~(0xFFFFUL << 32);
            sortKey |= (ulong)point3 << 32;
            matches.Add(rank with { SortKey = sortKey });
        }

        MatchDeltaRowsWithDirectory(snapshot, delta, querySegments, filePattern, fileQueryLen, matches, slab, directoryFilterLower);
    }

    // Delta churn is small, so it gets a plain per-row equivalent without the base-only row-index
    // shortcuts above -- correctness over throughput for a handful of live-updated rows.
    private static void MatchDeltaRowsWithDirectory(Snapshot snapshot, DeltaOverlay delta, string[] querySegments,
        FzfPattern? filePattern, int fileQueryLen, FzfTopN matches, FzfSlab slab, string? directoryFilterLower)
    {
        foreach (var record in delta.RowsToMatch())
        {
            if (record.Name.Length == 0)
                continue;
            FzfPatternResult fileMatch = default;
            if (filePattern != null && !SearchMatcher.TryMatchNameOrAliases(filePattern, record.Name, record.Aliases, record.ProviderIds, fileQueryLen, slab, out fileMatch))
                continue;

            var parentPath = delta.GetParentPath(record);
            var dirScore = VerifyPathSegmentsMatch(parentPath, querySegments, slab);
            if (dirScore <= 0)
                continue;
            var path = delta.GetFullPath(record);
            if (directoryFilterLower != null && !path.StartsWith(directoryFilterLower, StringComparison.OrdinalIgnoreCase))
                continue;

            fileMatch = fileMatch with { Score = fileMatch.Score + dirScore };
            var isOverride = delta.BaseOverrides.ContainsValue(record);
            var entryIndex = isOverride ? FindOverrideRow(delta, record) : snapshot.Count + delta.Added.IndexOf(record);
            if (entryIndex < 0)
                continue;

            var rank = FzfResultRank.ForDefaultScheme(entryIndex, record.Name, fileMatch);
            var relativeDepth = GetRelativeDepth(path, parentPath);
            var point3 = (ushort)((Math.Min(relativeDepth, 255) << 8) | (Math.Min(record.Name.Length, 255) & 0xFF));
            var sortKey = rank.SortKey;
            sortKey &= ~(0xFFFFUL << 32);
            sortKey |= (ulong)point3 << 32;
            matches.Add(rank with { SortKey = sortKey });
        }
    }

    private static int FindOverrideRow(DeltaOverlay delta, DeltaOverlay.DeltaRecord record)
    {
        foreach (var (row, r) in delta.BaseOverrides)
            if (ReferenceEquals(r, record))
                return row;
        return -1;
    }

    private static void SearchFilenameOnly(Snapshot snapshot, DeltaOverlay delta, string pathQuery, FzfTopN matches,
        FzfSlab slab, List<(string Alias, byte ProviderId)> aliasScratch, CancellationToken token, string? directoryFilterLower)
    {
        var pattern = FzfPattern.ParseText(pathQuery);
        var queryLen = pattern.GetTotalTermLength();

        for (var row = 0; row < snapshot.Count; row++)
        {
            token.ThrowIfCancellationRequested();
            if (snapshot.IsDeleted(row) || delta.IsSuperseded(row))
                continue;
            if (!SearchMatcher.MatchRow(snapshot, row, pattern, queryLen, slab, aliasScratch, out var name, out var match))
                continue;
            var path = delta.GetFullPath(row);
            if (directoryFilterLower != null && !path.StartsWith(directoryFilterLower, StringComparison.OrdinalIgnoreCase))
                continue;
            matches.Add(FzfResultRank.ForDefaultScheme(row, name, match));
        }

        foreach (var record in delta.RowsToMatch())
        {
            if (record.Name.Length == 0)
                continue;
            if (!SearchMatcher.TryMatchNameOrAliases(pattern, record.Name, record.Aliases, record.ProviderIds, queryLen, slab, out var match))
                continue;
            var path = delta.GetFullPath(record);
            if (directoryFilterLower != null && !path.StartsWith(directoryFilterLower, StringComparison.OrdinalIgnoreCase))
                continue;
            var isOverride = delta.BaseOverrides.ContainsValue(record);
            var entryIndex = isOverride ? FindOverrideRow(delta, record) : snapshot.Count + delta.Added.IndexOf(record);
            if (entryIndex < 0)
                continue;
            matches.Add(FzfResultRank.ForDefaultScheme(entryIndex, record.Name, match));
        }
    }

    // Mirrors PathExtensions.VerifyPathSegmentsMatch: matches query segments against the path's own
    // segments right-to-left, requiring every query segment to find a match somewhere along the path.
    private static int VerifyPathSegmentsMatch(string path, string[] querySegments, FzfSlab slab)
    {
        var pathSegments = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var qIdx = querySegments.Length - 1;
        var pIdx = pathSegments.Length - 1;
        var totalScore = 0;

        while (qIdx >= 0 && pIdx >= 0)
        {
            var pattern = FzfPattern.ParseText(querySegments[qIdx]);
            if (TryMatchSegmentWithAlias(pathSegments[pIdx], pattern, slab, out var score))
            {
                totalScore += score;
                qIdx--;
            }
            pIdx--;
        }
        return qIdx < 0 ? totalScore : 0;
    }

    // Ungated alias fallback (no IsAcceptableAliasMatch check) -- mirrors
    // PathExtensions.TryMatchSegmentWithAlias exactly; directory segments are short enough that a weak
    // coincidental alias hit isn't the risk name matching guards against.
    private static bool TryMatchSegmentWithAlias(string segment, FzfPattern pattern, FzfSlab slab, out int score)
    {
        score = 0;
        if (pattern.TryMatch(segment, out var match, FzfScoringScheme.Default, slab))
        {
            score = match.Score;
            return true;
        }
        if (!AliasProviderRegistry.HasNonAscii(segment))
            return false;
        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            try
            {
                if (!provider.CanHandle(segment))
                    continue;
                foreach (var alias in provider.GetAliases(segment))
                {
                    if (pattern.TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default, slab))
                    {
                        score = aliasMatch.Score;
                        return true;
                    }
                }
            }
            catch
            {
            }
        }
        return false;
    }

    private static int GetRelativeDepth(string path, string basePath)
    {
        var count = 0;
        for (var i = basePath.Length; i < path.Length; i++)
            if (path[i] == '\\' || path[i] == '/')
                count++;
        return count;
    }
}
