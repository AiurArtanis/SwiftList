namespace SwiftList.Core.SearchIndex.Fzf;

internal sealed class FzfRankCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RankCacheStore> _stores = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxSources;
    private readonly int _maxTermsPerSource;

    public FzfRankCache(int maxSources, int maxTermsPerSource)
    {
        _maxSources = Math.Max(1, maxSources);
        _maxTermsPerSource = Math.Max(1, maxTermsPerSource);
    }

    public bool TryGet(string sourceKey, int count, string term, int limit, out FzfRank[] ranks)
    {
        ranks = Array.Empty<FzfRank>();
        lock (_gate)
        {
            if (!_stores.TryGetValue(sourceKey, out var store))
                return false;

            if (store.Count != count)
            {
                _stores.Remove(sourceKey);
                return false;
            }

            if (!store.TryGet(term, limit, out var cachedRanks))
                return false;

            ranks = cachedRanks;
            return true;
        }
    }

    public void Store(string sourceKey, int count, string term, int limit, FzfRank[] ranks)
    {
        if (term.Length == 0 || ranks.Length == 0)
            return;

        lock (_gate)
        {
            if (_stores.Count > _maxSources)
                _stores.Clear();

            if (!_stores.TryGetValue(sourceKey, out var store) || store.Count != count)
            {
                store = new RankCacheStore(count, _maxTermsPerSource);
                _stores[sourceKey] = store;
            }

            store.Store(term, limit, ranks);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _stores.Clear();
        }
    }

    private sealed class RankCacheStore
    {
        private readonly Dictionary<string, RankCacheEntry> _entries = new(StringComparer.Ordinal);
        private readonly Queue<string> _order = new();
        private readonly int _maxTerms;

        public RankCacheStore(int count, int maxTerms)
        {
            Count = count;
            _maxTerms = maxTerms;
        }

        public int Count { get; }

        public bool TryGet(string term, int limit, out FzfRank[] ranks)
        {
            ranks = Array.Empty<FzfRank>();
            if (!_entries.TryGetValue(term, out var entry) || entry.Limit < limit)
                return false;

            ranks = entry.Ranks;
            return true;
        }

        public void Store(string term, int limit, FzfRank[] ranks)
        {
            if (_entries.ContainsKey(term))
            {
                _entries[term] = new RankCacheEntry(limit, ranks);
                return;
            }

            _entries[term] = new RankCacheEntry(limit, ranks);
            _order.Enqueue(term);

            while (_entries.Count > _maxTerms && _order.TryDequeue(out var oldest))
                _entries.Remove(oldest);
        }
    }

    private sealed record RankCacheEntry(int Limit, FzfRank[] Ranks);
}
