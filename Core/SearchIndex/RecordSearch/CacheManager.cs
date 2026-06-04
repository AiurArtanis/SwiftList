using System;
using System.Collections.Generic;
using SwiftList.Core.SearchIndex.Fzf;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.SearchIndex.RecordSearch
{
    internal sealed record CandidateCacheEntry(string Term, int Count, int[] Candidates);

    internal sealed class CandidateCacheStore
    {
        private readonly Dictionary<string, CandidateCacheEntry> _entries = new(StringComparer.Ordinal);
        private readonly Queue<string> _order = new();
        private const int MaxCandidateCacheTermsPerSource = 3;

        public CandidateCacheStore(int count)
        {
            Count = count;
        }

        public int Count { get; }

        public CandidateCacheEntry? FindBestPrefix(string term)
        {
            CandidateCacheEntry? best = null;
            foreach (var entry in _entries.Values)
            {
                if (term.Length <= entry.Term.Length ||
                    !term.StartsWith(entry.Term, StringComparison.Ordinal))
                {
                    continue;
                }

                if (best == null || entry.Term.Length > best.Term.Length)
                    best = entry;
            }

            return best;
        }

        public void Store(CandidateCacheEntry entry)
        {
            if (_entries.ContainsKey(entry.Term))
            {
                _entries[entry.Term] = entry;
                return;
            }

            _entries[entry.Term] = entry;
            _order.Enqueue(entry.Term);

            while (_entries.Count > MaxCandidateCacheTermsPerSource && _order.TryDequeue(out string? oldest))
                _entries.Remove(oldest);
        }
    }

    internal sealed class CacheManager
    {
        private const int MaxCachedCandidates = 2_000_000;
        private const int MaxCandidateCacheTermsPerSource = 3;

        private readonly object _candidateCacheGate = new();
        private readonly Dictionary<string, CandidateCacheStore> _candidateCaches = new(StringComparer.OrdinalIgnoreCase);
        private readonly FzfRankCache _rankCache = new(maxSources: 16, maxTermsPerSource: MaxCandidateCacheTermsPerSource);

        public void Clear()
        {
            lock (_candidateCacheGate)
            {
                _candidateCaches.Clear();
            }
            _rankCache.Clear();
        }

        public bool TryGetRankCache(RuntimeIndex index, string cacheTerm, int limit, out FzfRank[] ranks)
        {
            return _rankCache.TryGet(index.SourceKey, index.Count, cacheTerm, limit, out ranks);
        }

        public void StoreRankCache(RuntimeIndex index, string cacheTerm, List<FzfRank> ranks, int limit)
        {
            _rankCache.Store(index.SourceKey, index.Count, cacheTerm, limit, ranks.ToArray());
        }

        public bool CanCacheCandidates(
            FzfPattern pattern,
            string? directoryFilterLower,
            ulong? directoryRootId,
            out string cacheTerm)
        {
            cacheTerm = string.Empty;
            if (directoryFilterLower != null || directoryRootId != null)
                return false;

            if (pattern.TermSets.Length != 1)
                return false;

            var terms = pattern.TermSets[0].Terms;
            if (terms.Length != 1)
                return false;

            var term = terms[0];
            if (term.Inverse || term.Kind != FzfTermKind.Fuzzy)
                return false;

            cacheTerm = term.Text;
            return cacheTerm.Length > 0;
        }

        public CandidateCacheEntry? GetCandidateCache(RuntimeIndex index, string cacheTerm)
        {
            lock (_candidateCacheGate)
            {
                if (!_candidateCaches.TryGetValue(index.SourceKey, out var store))
                    return null;

                if (store.Count != index.Count)
                {
                    _candidateCaches.Remove(index.SourceKey);
                    return null;
                }

                return store.FindBestPrefix(cacheTerm);
            }
        }

        public void StoreCandidateCache(RuntimeIndex index, string cacheTerm, List<int>? candidates)
        {
            if (candidates == null || cacheTerm.Length == 0 || candidates.Count > MaxCachedCandidates)
                return;

            DeduplicateCandidateIndexes(candidates);
            var entry = new CandidateCacheEntry(cacheTerm, index.Count, candidates.ToArray());
            lock (_candidateCacheGate)
            {
                if (_candidateCaches.Count > 16)
                    _candidateCaches.Clear();

                if (!_candidateCaches.TryGetValue(index.SourceKey, out var store) || store.Count != index.Count)
                {
                    store = new CandidateCacheStore(index.Count);
                    _candidateCaches[index.SourceKey] = store;
                }

                store.Store(entry);
            }
        }

        private static void DeduplicateCandidateIndexes(List<int> candidates)
        {
            if (candidates.Count < 2)
                return;

            var seen = new HashSet<int>();
            int write = 0;
            for (int read = 0; read < candidates.Count; read++)
            {
                int candidate = candidates[read];
                if (!seen.Add(candidate))
                    continue;

                candidates[write++] = candidate;
            }

            if (write < candidates.Count)
                candidates.RemoveRange(write, candidates.Count - write);
        }
    }
}
