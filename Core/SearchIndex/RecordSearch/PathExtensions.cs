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
            var (bestDirIndex, bestDirScore) = FindBestDirectoryIndex(index, dirQuery, slab, token);
            if (bestDirIndex >= 0)
            {
                var filePattern = !string.IsNullOrEmpty(fileQuery) ? Helpers.GetPattern("p_file|" + fileQuery, fileQuery, parseText: true) : null;
                var directoryMembershipCache = new Dictionary<int, bool>();
                var basePath = index.GetFullPath(bestDirIndex);

                for (var i = 0; i < index.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if (index.IsDeleted(i))
                        continue;

                    if (!index.IsUnderDirectoryCached(i, bestDirIndex, directoryMembershipCache))
                        continue;

                    var name = index.GetName(i);
                    FzfPatternResult fileMatch = default;
                    if (filePattern != null && !filePattern.TryMatch(name, out fileMatch, FzfScoringScheme.Default, slab))
                        continue;

                    // Prioritize files in directories with higher match scores
                    fileMatch = fileMatch with { Score = fileMatch.Score + bestDirScore };

                    var path = index.GetFullPath(i);
                    if (directoryFilterLower != null && !path.StartsWith(directoryFilterLower, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var rank = FzfResultRank.ForDefaultScheme(i, name, fileMatch);
                    
                    // Prioritize shallower relative path depth under the matched directory
                    var relativeDepth = GetRelativeDepth(path, basePath);
                    var point3 = (ushort)((Math.Min(relativeDepth, 255) << 8) | (Math.Min(name.Length, 255) & 0xFF));
                    var sortKey = rank.SortKey;
                    sortKey &= ~(0xFFFFUL << 32); // Clear bits 32-47 (LengthPoint)
                    sortKey |= ((ulong)point3 << 32);
                    rank = rank with { SortKey = sortKey };

                    matches.Add(rank);
                }

                return index.Finish(matches.Finish(keep), limit);
            }
            else
            {
                return results;
            }
        }

        var pattern = Helpers.GetPattern("p_file_fallback|" + pathQuery, pathQuery, parseText: true);
        for (var i = 0; i < index.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (index.IsDeleted(i))
                continue;

            var name = index.GetName(i);
            if (!pattern.TryMatch(name, out var fileMatch, FzfScoringScheme.Default, slab))
                continue;

            var path = index.GetFullPath(i);
            if (directoryFilterLower != null && !path.StartsWith(directoryFilterLower, StringComparison.OrdinalIgnoreCase))
                continue;

            matches.Add(FzfResultRank.ForDefaultScheme(i, name, fileMatch));
        }

        return index.Finish(matches.Finish(keep), limit);
    }

    private static (int index, int score) FindBestDirectoryIndex(RuntimeIndex index, string dirQuery, FzfSlab slab, CancellationToken token)
    {
        var lastSep = dirQuery.LastIndexOf(Path.DirectorySeparatorChar);
        var lastSegment = lastSep >= 0 ? dirQuery.Substring(lastSep + 1) : dirQuery;

        var namePattern = Helpers.GetPattern("find_dir_name|" + lastSegment, lastSegment, parseText: true);
        var pathPattern = Helpers.GetPattern("find_dir_path|" + dirQuery, dirQuery, parseText: true);
        var querySegments = dirQuery.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

        var bestIdx = -1;
        var bestScore = -1;

        for (var i = 0; i < index.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (index.IsDeleted(i) || !index.IsDirectory(i))
                continue;

            var name = index.GetName(i);
            if (!namePattern.TryMatch(name, out _, FzfScoringScheme.Default, slab))
                continue;

            var path = index.GetFullPath(i);
            if (pathPattern.TryMatch(path, out var match, FzfScoringScheme.Path, slab))
            {
                if (!VerifyPathSegmentsMatch(path, querySegments, slab))
                    continue;

                if (match.Score > bestScore)
                {
                    bestScore = match.Score;
                    bestIdx = i;
                }
            }
        }

        // Filter out extremely sparse fuzzy matches that are likely false positives
        if (bestScore < dirQuery.Length * 10)
        {
            return (-1, -1);
        }

        return (bestIdx, bestScore);
    }

    private static bool VerifyPathSegmentsMatch(string path, string[] querySegments, FzfSlab slab)
    {
        var pathSegments = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var qIdx = querySegments.Length - 1;
        var pIdx = pathSegments.Length - 1;

        while (qIdx >= 0 && pIdx >= 0)
        {
            var qSeg = querySegments[qIdx];
            var pSeg = pathSegments[pIdx];

            var pattern = Helpers.GetPattern("verify_seg|" + qSeg, qSeg, parseText: true);
            if (pattern.TryMatch(pSeg, out _, FzfScoringScheme.Default, slab))
            {
                qIdx--;
            }
            pIdx--;
        }

        return qIdx < 0;
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

        if (!index.TryResolvePath(parsed.ExactPathLower, out var parentId, out var childPrefixLower))
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

        var pattern = childPrefixLower.Length == 0 ? null : Helpers.GetPattern("child|" + childPrefixLower, "^" + childPrefixLower, parseText: true);
        var matches = new FzfTopN(Math.Max(limit * 8, 64));
        var slab = new FzfSlab();
        foreach (var childIndex in index.EnumerateChildren(parentId))
        {
            token.ThrowIfCancellationRequested();
            var name = index.GetName(childIndex);
            if (index.IsDeleted(childIndex))
                continue;
            FzfPatternResult match = default;
            if (pattern != null && !pattern.TryMatch(name, out match, FzfScoringScheme.Default, slab))
                continue;

            matches.Add(pattern == null
                ? FzfResultRank.ForDefaultScheme(childIndex, name, new FzfPatternResult(0, 0, 0, 0, false))
                : FzfResultRank.ForDefaultScheme(childIndex, name, match));
        }

        foreach (var item in index.Finish(matches.Finish(Math.Max(limit * 8, 64)), limit))
            results.Add(item);
        return true;
    }
}
