using SwiftList.Core.SearchIndex.Fzf;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.SearchIndex.RecordSearch;

internal static class PathExtensions
{
    public static List<SearchResult> SearchPath(
        this Searcher searcher,
        RuntimeIndex index,
        ParsedSearchQuery parsed,
        int limit,
        CancellationToken token,
        string? directoryFilterLower)
    {
        var results = new List<SearchResult>(limit);
        if (index.TrySearchDirectoryChildren(parsed, limit, results, token))
            return results;

        if (parsed.TargetDrive != null && !parsed.TargetDrive.Equals(index.SourceKey, StringComparison.OrdinalIgnoreCase))
            return new List<SearchResult>();

        var pathQuery = parsed.PathPatternLower ?? string.Empty;
        var keep = Math.Max(limit * 8, 64);
        var matches = new FzfTopN(keep);
        var slab = new FzfSlab();

        var lastSep = pathQuery.LastIndexOf(Path.DirectorySeparatorChar);
        var dirQuery = lastSep >= 0 ? pathQuery.Substring(0, lastSep) : string.Empty;
        var fileQuery = lastSep >= 0 ? pathQuery.Substring(lastSep + 1) : pathQuery;

        if (!string.IsNullOrEmpty(dirQuery))
        {
            var filePattern = !string.IsNullOrEmpty(fileQuery) ? Helpers.GetPattern("p_file|" + fileQuery, fileQuery, parseText: true) : null;
            var querySegments = dirQuery.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < index.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (index.IsDeleted(i))
                    continue;

                FzfPatternResult fileMatch = default;
                string name;
                if (filePattern != null)
                {
                    if (!StreamingSearchExtensions.MatchCandidate(index, i, filePattern, slab, out name, out fileMatch))
                        continue;
                }
                else
                {
                    name = index.GetName(i);
                }

                var parentIndex = index.ParentIndexes[i];
                if (parentIndex < 0)
                    continue;

                var parentPath = index.GetFullPath(parentIndex);
                var dirScore = VerifyPathSegmentsMatch(parentPath, querySegments, slab);
                if (dirScore <= 0)
                    continue;

                // Prioritize files in directories with higher match scores
                fileMatch = fileMatch with { Score = fileMatch.Score + dirScore };

                var path = index.GetFullPath(i);
                if (directoryFilterLower != null && !path.StartsWith(directoryFilterLower, StringComparison.OrdinalIgnoreCase))
                    continue;

                var rank = FzfResultRank.ForDefaultScheme(i, name, fileMatch);
                
                // Prioritize shallower relative path depth under the matched directory
                var relativeDepth = GetRelativeDepth(path, parentPath);
                var point3 = (ushort)((Math.Min(relativeDepth, 255) << 8) | (Math.Min(name.Length, 255) & 0xFF));
                var sortKey = rank.SortKey;
                sortKey &= ~(0xFFFFUL << 32); // Clear bits 32-47 (LengthPoint)
                sortKey |= ((ulong)point3 << 32);
                rank = rank with { SortKey = sortKey };

                matches.Add(rank);
            }

            return index.Finish(matches.Finish(keep), limit);
        }

        var pattern = Helpers.GetPattern("p_file_fallback|" + pathQuery, pathQuery, parseText: true);
        for (var i = 0; i < index.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (index.IsDeleted(i))
                continue;

            if (!StreamingSearchExtensions.MatchCandidate(index, i, pattern, slab, out var name, out var fileMatch))
                continue;

            var path = index.GetFullPath(i);
            if (directoryFilterLower != null && !path.StartsWith(directoryFilterLower, StringComparison.OrdinalIgnoreCase))
                continue;

            matches.Add(FzfResultRank.ForDefaultScheme(i, name, fileMatch));
        }

        return index.Finish(matches.Finish(keep), limit);
    }



    private static bool TryMatchSegmentWithAlias(string segment, FzfPattern pattern, FzfSlab slab, out int score)
    {
        score = 0;
        if (pattern.TryMatch(segment, out var match, FzfScoringScheme.Default, slab))
        {
            score = match.Score;
            return true;
        }

        if (AliasProviderRegistry.HasNonAscii(segment))
        {
            foreach (var provider in AliasProviderRegistry.GetActiveProviders())
            {
                try
                {
                    if (provider.CanHandle(segment))
                    {
                        foreach (var alias in provider.GetAliases(segment))
                        {
                            if (pattern.TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default, slab))
                            {
                                score = aliasMatch.Score;
                                return true;
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }
        return false;
    }

    private static int VerifyPathSegmentsMatch(string path, string[] querySegments, FzfSlab slab)
    {
        var pathSegments = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var qIdx = querySegments.Length - 1;
        var pIdx = pathSegments.Length - 1;
        var totalScore = 0;

        while (qIdx >= 0 && pIdx >= 0)
        {
            var qSeg = querySegments[qIdx];
            var pSeg = pathSegments[pIdx];

            var pattern = Helpers.GetPattern("verify_seg|" + qSeg, qSeg, parseText: true);
            if (TryMatchSegmentWithAlias(pSeg, pattern, slab, out var score))
            {
                totalScore += score;
                qIdx--;
            }
            pIdx--;
        }

        return qIdx < 0 ? totalScore : 0;
    }

    private static int GetRelativeDepth(string path, string basePath)
    {
        var count = 0;
        for (var i = basePath.Length; i < path.Length; i++)
        {
            if (path[i] == '\\' || path[i] == '/')
            {
                count++;
            }
        }
        return count;
    }

    private static bool TrySearchDirectoryChildren(
        this RuntimeIndex index,
        ParsedSearchQuery parsed,
        int limit,
        List<SearchResult> results,
        CancellationToken token)
    {
        if (parsed.ExactPathLower == null || parsed.TargetDrive == null)
            return false;

        if (!index.TryResolvePath(parsed.ExactPathLower, out var parentId, out var childPrefixLower, forceLastSegmentAsQuery: !parsed.PathEndsWithSeparator))
            return false;

        if (childPrefixLower.Length == 0)
        {
            if (index.TryGetIndexById(parentId, out var parentIndex))
            {
                var flags = (FileRecordFlags)index.Flags[parentIndex];
                results.Add(new SearchResult
                {
                    Name = index.GetName(parentIndex),
                    Path = index.GetFullPath(parentIndex),
                    IsDir = index.IsDirectory(parentIndex),
                    Drive = index.SourceKey,
                    Attributes = FileRecordFlagsHelper.ToAttributes(flags),
                    RankSortKey = 0
                });
            }
        }

        var pattern = childPrefixLower.Length == 0 ? null : Helpers.GetPattern("child|" + childPrefixLower, childPrefixLower, parseText: true);
        var matches = new FzfTopN(Math.Max(limit * 8, 64));
        var slab = new FzfSlab();
        foreach (var childIndex in index.EnumerateChildren(parentId))
        {
            token.ThrowIfCancellationRequested();
            if (index.IsDeleted(childIndex))
                continue;

            FzfPatternResult match = default;
            string name;
            if (pattern != null)
            {
                if (!StreamingSearchExtensions.MatchCandidate(index, childIndex, pattern, slab, out name, out match))
                    continue;
            }
            else
            {
                name = index.GetName(childIndex);
            }

            matches.Add(pattern == null
                ? FzfResultRank.ForDefaultScheme(childIndex, name, new FzfPatternResult(0, 0, 0, 0, false))
                : FzfResultRank.ForDefaultScheme(childIndex, name, match));
        }

        foreach (var item in index.Finish(matches.Finish(Math.Max(limit * 8, 64)), limit))
            results.Add(item);
        return true;
    }
}
