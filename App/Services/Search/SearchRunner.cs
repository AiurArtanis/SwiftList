using System.IO;
using SwiftList.Core;
namespace SwiftList.App.Services;

public class SearchRunner : IDisposable
{
    private readonly SearchService _searchService;
    private CancellationTokenSource? _searchCts;
    private readonly object _searchLock = new();

    public SearchRunner(SearchService searchService) => _searchService = searchService;

    public void QueueSearch(

        string query,
        int fileLimit,
        int appLimit,
        Action<bool> onSearchStateChanged,
        Action<List<AppSearchResult>, bool> onResultsUpdated,
        Action onServiceUnavailable)
    {
        lock (_searchLock)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
        }

        var cts = _searchCts;
        var token = cts.Token;

        // Short debounce for typing

        _ = Task.Delay(150, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            if (string.IsNullOrWhiteSpace(query))
            {
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    onSearchStateChanged(false);
                    onResultsUpdated(new List<AppSearchResult>(), true);
                }));
                return;
            }

            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => onSearchStateChanged(true)));

            _ = Task.Run(async () =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    await PerformStreamingSearchAsync(query, fileLimit, appLimit, onResultsUpdated, onServiceUnavailable, token);
                }

                catch (OperationCanceledException) { }

                catch (Exception ex)
                {
                    Logger.Log($"[SearchRunner] Search failed: {ex.Message}", LogLevel.Error);
                }

                finally
                {
                    _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        lock (_searchLock)
                        {
                            if (_searchCts == cts)
                            {
                                onSearchStateChanged(false);
                            }
                        }

                    }));
                }

            }, token);
        }, token);
    }

    private async Task<bool> PerformStreamingSearchAsync(

        string query,
        int fileLimit,
        int appLimit,
        Action<List<AppSearchResult>, bool> onResultsUpdated,
        Action onServiceUnavailable,
        CancellationToken token)
    {
        var uiResults = new List<AppSearchResult>();
        object resultsLock = new();
        long lastRenderTicks = 0;

        void RenderSnapshot(bool final)
        {
            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (token.IsCancellationRequested)
                    return;
                List<AppSearchResult> snapshot;

                lock (resultsLock)

                    snapshot = new List<AppSearchResult>(uiResults);
                onResultsUpdated(snapshot, final);
            }));
        }

        var ok = await _searchService.SearchStreamingAsync(query, fileLimit, appLimit, null, (result, isApplication) =>
        {
            token.ThrowIfCancellationRequested();

            lock (resultsLock)

                uiResults.Add(CreateUiResult(result, query, uiResults.Count));
            var now = Environment.TickCount64;
            var previous = Interlocked.Read(ref lastRenderTicks);
            if (now - previous >= 100 && Interlocked.CompareExchange(ref lastRenderTicks, now, previous) == previous)
                RenderSnapshot(final: false);
        }, token);
        if (!ok)
        {
            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!token.IsCancellationRequested)
                    onServiceUnavailable();
            }));
            return true;
        }

        token.ThrowIfCancellationRequested();
        RenderSnapshot(final: true);
        return true;
    }

    private static AppSearchResult CreateUiResult(SearchResult item, string query, int index) => new AppSearchResult
    {
        Name = string.IsNullOrWhiteSpace(item.Name) ? item.Path : item.Name,
        FullPath = item.Path,
        ParentDir = Path.GetDirectoryName(item.Path) ?? string.Empty,
        IsDir = item.IsDir,
        Drive = item.Drive,
        ResultKind = "File",
        Index = index,
        SearchQuery = query

    };

    public void Cancel()
    {
        lock (_searchLock)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
        }
    }

    public void Dispose() => Cancel();
}
