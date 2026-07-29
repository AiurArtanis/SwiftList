using SwiftList.Core;
using SwiftList.App.Services;
using SwiftList.Core.Services.Search;

using SwiftList.App.ViewModels.Search.Mapping;
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
        Func<List<SearchResult>?, string?, List<AppSearchResult>> resultMapper,
        Action<bool> onSearchStateChanged,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        Action? onLocalServiceUnavailable = null,
        Func<bool>? shouldEmitInstantResults = null,
        bool bypassExclusions = false)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        var delay = string.IsNullOrEmpty(query) || query.Length <= 1 ? 0 : (fileLimit > 100 ? 150 : 30);
        if (delay == 0)
        {
            PerformSearch(query, searchScope, isInlineSearchContext, fileLimit, appLimit, resultMapper, onSearchStateChanged, onResultsUpdated, onLocalServiceUnavailable, shouldEmitInstantResults, bypassExclusions);
        }
        else
        {
            _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    PerformSearch(query, searchScope, isInlineSearchContext, fileLimit, appLimit, resultMapper, onSearchStateChanged, onResultsUpdated, onLocalServiceUnavailable, shouldEmitInstantResults, bypassExclusions)));
            }, cts.Token);
        }
    }

    public void PerformSearch(
        string query,
        string? searchScope,
        bool isInlineSearchContext,
        int fileLimit,
        int appLimit,
        Func<List<SearchResult>?, string?, List<AppSearchResult>> resultMapper,
        Action<bool> onSearchStateChanged,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        Action? onLocalServiceUnavailable = null,
        Func<bool>? shouldEmitInstantResults = null,
        bool bypassExclusions = false)
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

        // Show instant-provider results (web URL, calculator, env vars, …) ahead of the file search
        // rather than waiting for it to stream in -- a query like a pasted URL may match no files at
        // all, so the streaming render never fires until the whole search finishes.
        //
        // Off the UI thread, even though this used to run inline here because the providers were
        // assumed cheap and synchronous. They are neither, necessarily: a provider is third-party code,
        // and PluginSearchResultMapper then probes whatever path it hands back with File.Exists /
        // Directory.Exists and asks the shell for its icon. Any of those blocks for the SMB timeout --
        // tens of seconds -- when the path is a UNC or a mapped drive whose server is gone, which froze
        // the window mid-keystroke and then released it on its own once the timeout expired. The same
        // call already runs on a background thread through BuildQuickResults on every streaming render,
        // so this only makes the two paths agree.
        _ = Task.Run(() =>
        {
            var instantResults = new List<AppSearchResult>();
            PluginSearchResultMapper.AddInstantResults(instantResults, query, null, isInlineSearchContext);
            if (instantResults.Count == 0 || token.IsCancellationRequested)
                return;

            _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                // Re-checked here rather than only above: this render is now asynchronous, so a newer
                // keystroke's results can already be on screen by the time it arrives, and painting
                // this stale instant-only snapshot over them would collapse them away.
                if (token.IsCancellationRequested || searchVersion != Volatile.Read(ref _searchVersion))
                    return;

                // Emit up-front only when the caller opts in -- the quick window allows this only while
                // its list is empty. During continuous typing the list already has rows, and an
                // instant-only snapshot would collapse the existing file rows away and then re-expand
                // them on the next (file) render -- that's the flicker. When skipped, the upcoming file
                // render still includes the instant results, so nothing is lost, just no separate
                // collapsing frame. Evaluated on the UI thread because it reads that list's state.
                if (shouldEmitInstantResults?.Invoke() ?? true)
                    onResultsUpdated(instantResults, string.Empty, false);
            }));
        }, token);

        _ = Task.Run(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var tracker = InlineSearchManager.Instance.ExplorerTracker;
                var dialogAdapter = tracker.ActiveAdapter;
                if (isInlineSearchContext && tracker.ActiveHwnd != IntPtr.Zero)
                {
                    var contextDirectory = !string.IsNullOrWhiteSpace(searchScope) ? searchScope : (tracker.ActivePath ?? tracker.LastActiveExplorerPath);
                    if (tracker.IsActiveWindowExplorer || (tracker.IsActiveWindowDialog && dialogAdapter != null))
                    {
                        var localMatches = new List<AppSearchResult>();
                        Task? localSearchTask = null;
                        if (!string.IsNullOrEmpty(contextDirectory))
                        {
                            // Always bypass ExcludedPaths/glob/regex filtering for the "current folder"
                            // section -- the user is explicitly looking at contextDirectory in Explorer
                            // right now, so global exclusion settings (meant to keep noise out of broad,
                            // untargeted searches) have no business hiding results from the one folder
                            // they're actually standing in.
                            localSearchTask = ExplorerSearchHelper.SearchLocalMatchesAsync(
                                _searchService, query, fileLimit, appLimit, contextDirectory, localMatches, token, bypassExclusions: true);
                        }
                        await PerformStreamingSearchAsync(query, null, contextDirectory, isInlineSearchContext, fileLimit, appLimit, resultMapper, searchVersion, onResultsUpdated, token, localMatches, localSearchTask, onLocalServiceUnavailable, bypassExclusions);
                        return;
                    }
                }
                var streamingScope = tracker.IsActiveWindowExplorer ? searchScope : null;
                var streamingContextDirectory = isInlineSearchContext ? (!string.IsNullOrWhiteSpace(searchScope) ? searchScope : tracker.ActivePath ?? tracker.LastActiveExplorerPath) : tracker.LastActiveExplorerPath;
                await PerformStreamingSearchAsync(query, streamingScope, streamingContextDirectory, isInlineSearchContext, fileLimit, appLimit, resultMapper, searchVersion, onResultsUpdated, token, null, null, onLocalServiceUnavailable, bypassExclusions);
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
        Func<List<SearchResult>?, string?, List<AppSearchResult>> resultMapper,
        int searchVersion,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        CancellationToken token,
        List<AppSearchResult>? localMatches = null,
        Task? localSearchTask = null,
        Action? onLocalServiceUnavailable = null,
        bool bypassExclusions = false)
    {
        var streamedResponse = new List<SearchResult>();
        object responseLock = new();
        var streamedCount = 0;
        var renderState = 0;
        var startTime = Environment.TickCount;

        // Both RenderSnapshot callers (the streaming result callback below, and the final call after
        // localSearchTask) already run on a background thread -- PerformStreamingSearchAsync executes
        // inside PerformSearch's Task.Run, which doesn't flow a SynchronizationContext, so every await
        // in this method resumes on a ThreadPool thread throughout. resultMapper (rank-sorts up to
        // FullSearchFileLimit=1000 results via SearchResultRankComparer, then builds one AppSearchResult
        // per row) and the rest of the snapshot-building logic below have no WPF/UI-thread dependency at
        // all -- they only read the locked snapshot and build plain lists. This used to run wrapped
        // inside the Dispatcher.BeginInvoke below regardless, meaning that CPU cost blocked the UI thread
        // on every streaming render for the full window's own broad queries. Now only the actual
        // UI-touching step (onResultsUpdated, which assigns FilteredResults and triggers sort/filter/
        // render) is marshaled, after re-checking staleness once more in case the search was superseded
        // while this thread was busy computing.
        void RenderSnapshot(bool final)
        {
            if (searchVersion != Volatile.Read(ref _searchVersion) || token.IsCancellationRequested)
                return;
            List<SearchResult> snapshot;
            lock (responseLock)
            {
                snapshot = new List<SearchResult>(streamedResponse);
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
                statusText = SearchResultMapper.FormatSearchStatus(0, snapshot.Count);
            else if (final)
                statusText = "No matching results";

            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (searchVersion != Volatile.Read(ref _searchVersion) || token.IsCancellationRequested)
                    return;
                onResultsUpdated(uiResults, statusText, final);
            }));
        }

        await _searchService.SearchStreamingAsync(query, fileLimit, appLimit, searchScope, result =>
        {
            token.ThrowIfCancellationRequested();

            // Excludes a result whose source (local drive, network drive, WSL, folder index) was found
            // unreachable by this session's own SearchReachabilityGate.BeginSession probe -- see its own
            // comment on why that's a better fit here than either source type's existing periodic signal.
            if (!SearchReachabilityGate.IsResultReachable(result))
                return;

            lock (responseLock)
            {
                streamedResponse.Add(result);
                streamedCount++;
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
        })), bypassExclusions);

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
