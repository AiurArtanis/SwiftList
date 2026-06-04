using System;
using System.Collections.Generic;
using System.Threading;
using SwiftList.Core.SearchIndex.Fzf;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.SearchIndex.RecordSearch
{
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
            var pattern = Helpers.GetPattern("p|" + pathQuery, pathQuery, parseText: true);
            int keep = Math.Max(limit * 8, 64);
            var matches = new FzfTopN(keep);
            var slab = new FzfSlab();

            for (int i = 0; i < index.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (index.IsDeleted(i))
                    continue;

                string path = index.GetFullPath(i);
                if (directoryFilterLower != null && !path.StartsWith(directoryFilterLower, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!pattern.TryMatch(path, out var match, FzfScoringScheme.Path, slab))
                    continue;

                matches.Add(FzfResultRank.ForPathScheme(i, path, match));
            }

            return index.Finish(matches.Finish(keep), limit);
        }

        private static bool TrySearchDirectoryChildren(
            this RuntimeIndex index,
            ParsedSearchQuery parsed,
            int limit,
            List<SearchResult> results,
            CancellationToken token)
        {
            if (!parsed.PathEndsWithSeparator || parsed.ExactPathLower == null || parsed.TargetDrive == null)
                return false;

            if (!index.TryResolvePath(parsed.ExactPathLower, out ulong parentId, out string childPrefixLower))
                return false;

            var pattern = childPrefixLower.Length == 0 ? null : Helpers.GetPattern("child|" + childPrefixLower, "^" + childPrefixLower, parseText: true);
            var matches = new FzfTopN(Math.Max(limit * 8, 64));
            var slab = new FzfSlab();
            int childCount = 0;
            foreach (int childIndex in index.EnumerateChildren(parentId))
            {
                childCount++;
                token.ThrowIfCancellationRequested();
                string name = index.GetName(childIndex);
                if (index.IsDeleted(childIndex))
                    continue;
                FzfPatternResult match = default;
                if (pattern != null && !pattern.TryMatch(name, out match, FzfScoringScheme.Default, slab))
                    continue;

                matches.Add(pattern == null
                    ? FzfResultRank.ForDefaultScheme(childIndex, name, new FzfPatternResult(0, 0, 0, 0, false))
                    : FzfResultRank.ForDefaultScheme(childIndex, name, match));
            }

            if (childCount == 0)
                return false;

            foreach (var item in index.Finish(matches.Finish(Math.Max(limit * 8, 64)), limit))
                results.Add(item);
            return true;
        }
    }
}
