using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SwiftList.Core.SearchIndex.Fzf;
using SwiftList.Core.SearchIndex.RecordSearch;

namespace SwiftList.Core.Indexer.NetworkDrive
{
    public static class NetworkIndexerSearchExtensions
    {
        public static List<SearchResult> Search(
            this NetworkIndexer indexer,
            string query,
            int limit,
            CancellationToken token = default,
            string? directoryFilter = null)
        {
            indexer.EnsureConfigured();
            if (limit <= 0 || string.IsNullOrWhiteSpace(query))
                return new List<SearchResult>();

            NetworkIndex[] snapshots;
            lock (indexer.Gate)
                snapshots = indexer._indexes.Values.ToArray();

            if (snapshots.Length == 0)
                return new List<SearchResult>();

            var parsed = SearchQueryParser.Parse(query);
            string? directoryFilterLower = IndexerHelper.NormalizeFilter(directoryFilter);
            var results = new List<SearchResult>(Math.Min(limit, 64));

            foreach (var index in snapshots)
            {
                token.ThrowIfCancellationRequested();
                if (!IsDriveAllowed(index.Drive, parsed, directoryFilterLower))
                    continue;

                index.Search(parsed, query, directoryFilterLower, limit, results, token);
            }

            results.Sort(FzfResultRank.CompareResults);
            if (results.Count > limit)
                results.RemoveRange(limit, results.Count - limit);

            return results;
        }

        private static bool IsDriveAllowed(string indexDrive, ParsedSearchQuery parsed, string? directoryFilterLower)
        {
            if (parsed.TargetDrive != null && !parsed.TargetDrive.Equals(indexDrive, StringComparison.OrdinalIgnoreCase))
                return false;

            if (directoryFilterLower == null)
                return true;

            return directoryFilterLower.StartsWith(indexDrive + @":\", StringComparison.OrdinalIgnoreCase);
        }
    }
}
