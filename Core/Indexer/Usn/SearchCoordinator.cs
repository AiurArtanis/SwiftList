using SwiftList.Core.SearchIndex.RecordIndex;
using SwiftList.Core.SearchIndex.RecordSearch;
using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.Indexer.Usn;

internal static class SearchCoordinator
{
    private static readonly Searcher _recordSearcher = new();

    public static List<SearchResult> Search(
        Dictionary<string, RuntimeIndex> recordIndexes,
        object lockObj,
        string query,
        int limit,
        CancellationToken token,
        string? directoryFilter)
    {
        RuntimeIndex[] snapshots;
        lock (lockObj)
            snapshots = recordIndexes.Values.ToArray();

        if (snapshots.Length == 0)
            return new List<SearchResult>();

        if (snapshots.Length == 1)
        {
            var results = new List<SearchResult>(limit);
            results.AddRange(_recordSearcher.Search(snapshots[0], query, limit, token, directoryFilter));
            results.Sort(FzfResultRank.CompareResults);
            if (results.Count > limit)
                results.RemoveRange(limit, results.Count - limit);
            return results;
        }

        var perDrive = new List<SearchResult>[snapshots.Length];
        Parallel.For(
            0,
            snapshots.Length,
            new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Math.Min(snapshots.Length, Math.Clamp(Environment.ProcessorCount, 2, 8))
            },
            i =>
            {
                token.ThrowIfCancellationRequested();
                perDrive[i] = _recordSearcher.Search(snapshots[i], query, limit, token, directoryFilter);
            });

        var merged = new List<SearchResult>(Math.Min(limit * snapshots.Length, limit * 4));
        foreach (var driveResults in perDrive)
        {
            token.ThrowIfCancellationRequested();
            if (driveResults != null)
                merged.AddRange(driveResults);
        }

        merged.Sort(FzfResultRank.CompareResults);
        if (merged.Count > limit)
            merged.RemoveRange(limit, merged.Count - limit);
        return merged;
    }

    public static void SearchStreaming(
        Dictionary<string, RuntimeIndex> recordIndexes,
        object lockObj,
        string query,
        int limit,
        Action<SearchResult> onResult,
        CancellationToken token,
        string? directoryFilter)
    {
        RuntimeIndex[] snapshots;
        lock (lockObj)
            snapshots = recordIndexes.Values.ToArray();

        if (snapshots.Length == 0)
            return;

        if (snapshots.Length == 1)
        {
            _recordSearcher.SearchStreaming(snapshots[0], query, limit, onResult, token, directoryFilter);
            return;
        }

        var writeLock = new object();
        Parallel.For(
            0,
            snapshots.Length,
            new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Math.Min(snapshots.Length, Math.Clamp(Environment.ProcessorCount, 2, 8))
            },
            i =>
            {
                token.ThrowIfCancellationRequested();
                _recordSearcher.SearchStreaming(snapshots[i], query, limit, result =>
                {
                    token.ThrowIfCancellationRequested();
                    lock (writeLock)
                    {
                        token.ThrowIfCancellationRequested();
                        onResult(result);
                    }
                }, token, directoryFilter);
            });
    }

    public static void ClearCaches() => _recordSearcher.ClearCaches();
}
