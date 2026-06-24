using SwiftList.Core.SearchIndex.RecordIndex;
using SwiftList.Core.SearchIndex.RecordSearch;

namespace SwiftList.Core.Indexer.Usn;

internal static class SearchCoordinator
{
    private static readonly Searcher _recordSearcher = new();


    public static void SearchStreaming(
        Dictionary<string, RuntimeIndex> recordIndexes,
        object lockObj,
        string query,
        int limit,
        Action<SearchResult> onResult,
        CancellationToken token,
        string? directoryFilter)
    {
        lock (lockObj)
        {
            var snapshots = recordIndexes.Values.ToArray();

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
    }

    public static void ClearCaches() => _recordSearcher.ClearCaches();
}
