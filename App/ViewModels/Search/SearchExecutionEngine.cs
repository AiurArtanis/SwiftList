using SwiftList.Core;
using SwiftList.App.Services;
namespace SwiftList.App.ViewModels.Search;

internal sealed class SearchExecutionEngine : IDisposable
{
    private readonly SearchService _searchService;
    private CancellationTokenSource? _searchCts;
    private readonly object _searchCtsLock = new();
    private CancellationTokenSource? _debounceCts;
    private int _searchVersion;

    public SearchExecutionEngine(SearchService searchService) => _searchService = searchService;

    public void QueueSearch(
        string query,
        string? searchScope,
        bool isInlineSearchContext,
        int fileLimit,
        int appLimit,
        Func<SearchResponse, string?, List<AppSearchResult>> resultMapper,
        Action<bool> onSearchStateChanged,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        Action? onLocalServiceUnavailable = null)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        var delay = string.IsNullOrEmpty(query) || query.Length <= 1 ? 0 : (fileLimit > 100 ? 150 : 30);
        if (delay == 0)
        {
            PerformSearch(query, searchScope, isInlineSearchContext, fileLimit, appLimit, resultMapper, onSearchStateChanged, onResultsUpdated, onLocalServiceUnavailable);
        }
        else
        {
            _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    PerformSearch(query, searchScope, isInlineSearchContext, fileLimit, appLimit, resultMapper, onSearchStateChanged, onResultsUpdated, onLocalServiceUnavailable)));
            }, cts.Token);
        }
    }

    public void PerformSearch(
        string query,
        string? searchScope,
        bool isInlineSearchContext,
        int fileLimit,
        int appLimit,
        Func<SearchResponse, string?, List<AppSearchResult>> resultMapper,
        Action<bool> onSearchStateChanged,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        Action? onLocalServiceUnavailable = null)
    {
        Logger.Log($"[SearchExecutionEngine] Performing search: '{query}', scope: '{searchScope}'", LogLevel.Debug);
        CancelPendingSearch();
        if (string.IsNullOrWhiteSpace(query))
        {
            onSearchStateChanged(false);
            onResultsUpdated(new List<AppSearchResult>(), string.Empty, true);
            return;
        }

        onSearchStateChanged(true);
        var cts = new CancellationTokenSource();
        var searchVersion = Interlocked.Increment(ref _searchVersion);

        lock (_searchCtsLock)
        {
            _searchCts = cts;
        }

        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var tracker = InlineSearchManager.Instance.ExplorerTracker;
                var adapter = tracker.ActiveInlineAdapter;
                if (isInlineSearchContext && adapter != null && tracker.ActiveHwnd != IntPtr.Zero)
                {
                    var contextDirectory = !string.IsNullOrWhiteSpace(searchScope) ? searchScope : (tracker.ActivePath ?? tracker.LastActiveExplorerPath);
                    if (tracker.IsActiveWindowExplorer)
                    {
                        var localMatches = new List<AppSearchResult>();
                        Task? localSearchTask = null;
                        if (!string.IsNullOrEmpty(contextDirectory))
                        {
                            localSearchTask = ExplorerSearchHelper.SearchLocalMatchesAsync(
                                _searchService, query, fileLimit, appLimit, contextDirectory, localMatches, token);
                        }
                        await PerformStreamingSearchAsync(query, null, contextDirectory, isInlineSearchContext, fileLimit, appLimit, resultMapper, searchVersion, onResultsUpdated, token, localMatches, localSearchTask, onLocalServiceUnavailable);
                        return;
                    }
                    else
                    {
                        var listItems = adapter.GetListItems(tracker.ActiveHwnd).ToList();
                        if (listItems.Count > 0)
                        {
                            InlineListSearchHelper.PerformInlineListProviderSearch(query, adapter, tracker.ActiveHwnd, listItems, contextDirectory, searchVersion, () => Volatile.Read(ref _searchVersion), onResultsUpdated, token);
                            return;
                        }
                    }
                }
                var streamingScope = tracker.IsActiveWindowExplorer ? searchScope : null;
                var streamingContextDirectory = isInlineSearchContext ? (!string.IsNullOrWhiteSpace(searchScope) ? searchScope : tracker.ActivePath ?? tracker.LastActiveExplorerPath) : tracker.LastActiveExplorerPath;
                await PerformStreamingSearchAsync(query, streamingScope, streamingContextDirectory, isInlineSearchContext, fileLimit, appLimit, resultMapper, searchVersion, onResultsUpdated, token, null, null, onLocalServiceUnavailable);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Log($"[SearchExecutionEngine] PerformSearch failed: {ex}", LogLevel.Error);
            }
            finally
            {
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    lock (_searchCtsLock)
                    {
                        if (_searchCts == cts)
                        {
                            onSearchStateChanged(false);
                        }
                    }
                }));
            }
        }, token);
    }

    private async Task PerformStreamingSearchAsync(
        string query,
        string? searchScope,
        string? contextDirectory,
        bool isInlineSearchContext,
        int fileLimit,
        int appLimit,
        Func<SearchResponse, string?, List<AppSearchResult>> resultMapper,
        int searchVersion,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        CancellationToken token,
        List<AppSearchResult>? localMatches = null,
        Task? localSearchTask = null,
        Action? onLocalServiceUnavailable = null)
    {
        var streamedResponse = new SearchResponse();
        object responseLock = new();
        var streamedCount = 0;
        var renderState = 0;
        var startTime = Environment.TickCount;

        void RenderSnapshot(bool final)
        {
            void ApplySnapshot()
            {
                if (searchVersion != Volatile.Read(ref _searchVersion) || token.IsCancellationRequested)
                    return;
                SearchResponse snapshot;

                lock (responseLock)
                {
                    snapshot = new SearchResponse
                    {
                        FileResults = new List<SearchResult>(streamedResponse.FileResults)
                    };
                }

                var uiResults = resultMapper(snapshot, contextDirectory);

                List<AppSearchResult>? localMatchesCopy = null;
                if (localMatches != null)
                {
                    lock (localMatches)
                    {
                        if (localMatches.Count > 0)
                        {
                            localMatchesCopy = new List<AppSearchResult>(localMatches);
                        }
                    }
                }

                if (localMatchesCopy != null)
                {
                    uiResults = InlineListSearchHelper.MergeLocalMatches(uiResults, localMatchesCopy, query);
                }


                if (final && uiResults.Count == 0)
                    uiResults.Add(SearchResultMapper.CreateNoResultsResult(query));
                var statusText = "";
                if (uiResults.Count > 0)
                    statusText = SearchResultMapper.FormatSearchStatus(0, snapshot.FileResults.Count);
                else if (final)
                    statusText = "No matching results";
                onResultsUpdated(uiResults, statusText, final);
            }

            if (final)
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(ApplySnapshot));
            else
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(ApplySnapshot));
        }

        await _searchService.SearchStreamingAsync(query, fileLimit, appLimit, searchScope, (result, isApplication) =>
        {
            token.ThrowIfCancellationRequested();

            lock (responseLock)
            {
                if (!isApplication)
                {
                    streamedResponse.FileResults.Add(result);
                    streamedCount++;
                }
            }

            if (Volatile.Read(ref renderState) == 0 && Volatile.Read(ref streamedCount) < 9)
                return;
            if (Interlocked.CompareExchange(ref renderState, 1, 0) == 0)
            {
                var elapsed = Environment.TickCount - startTime;
                if (elapsed < 40)
                {
                    _ = Task.Delay(40 - elapsed, token).ContinueWith(t =>
                    {
                        if (t.IsCanceled) return;
                        if (Volatile.Read(ref renderState) == 1)
                        {
                            RenderSnapshot(final: false);
                        }
                    }, token);
                }
                else
                {
                    RenderSnapshot(final: false);
                }
            }

        }, token, () => _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!token.IsCancellationRequested && searchVersion == Volatile.Read(ref _searchVersion))
            {
                onLocalServiceUnavailable?.Invoke();
            }
        })));

        token.ThrowIfCancellationRequested();
        if (localSearchTask != null)
        {
            try
            {
                await localSearchTask;
            }
            catch { }
        }
        Interlocked.Exchange(ref renderState, 2);
        RenderSnapshot(final: true);
    }

    public void CancelPendingSearch()
    {
        try { _debounceCts?.Cancel(); _debounceCts?.Dispose(); _debounceCts = null; } catch { }
        lock (_searchCtsLock)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
        }
    }

    public void Dispose() { CancelPendingSearch(); _debounceCts?.Dispose(); }
}
