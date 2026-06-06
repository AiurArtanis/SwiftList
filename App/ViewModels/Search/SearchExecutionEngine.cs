using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SwiftList.Core;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Search
{
    internal sealed class SearchExecutionEngine : IDisposable
    {
        private readonly SearchService _searchService;
        private CancellationTokenSource? _searchCts;
        private readonly object _searchCtsLock = new();
        private CancellationTokenSource? _debounceCts;
        private int _searchVersion;

        public SearchExecutionEngine(SearchService searchService)
        {
            _searchService = searchService;
        }

        public void QueueSearch(
            string query,
            string? searchScope,
            bool isInlineSearchContext,
            Action<bool> onSearchStateChanged,
            Action<List<AppSearchResult>, string, bool> onResultsUpdated,
            Action onServiceUnavailable)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();

            var cts = new CancellationTokenSource();
            _debounceCts = cts;

            Task.Delay(35, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    PerformSearch(query, searchScope, isInlineSearchContext, onSearchStateChanged, onResultsUpdated, onServiceUnavailable));
            }, cts.Token);
        }

        public void PerformSearch(
            string query,
            string? searchScope,
            bool isInlineSearchContext,
            Action<bool> onSearchStateChanged,
            Action<List<AppSearchResult>, string, bool> onResultsUpdated,
            Action onServiceUnavailable)
        {
            CancelPendingSearch();

            if (string.IsNullOrWhiteSpace(query))
            {
                onSearchStateChanged(false);
                onResultsUpdated(new List<AppSearchResult>(), string.Empty, true);
                return;
            }

            onSearchStateChanged(true);

            var cts = new CancellationTokenSource();
            int searchVersion = Interlocked.Increment(ref _searchVersion);
            lock (_searchCtsLock)
            {
                _searchCts = cts;
            }
            var token = cts.Token;

            Task.Run(() =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    var tracker = InlineSearchManager.Instance.ExplorerTracker;
                    var adapter = tracker.ActiveInlineAdapter;
                    if (isInlineSearchContext && adapter != null && tracker.ActiveHwnd != IntPtr.Zero)
                    {
                        var listItems = adapter.GetListItems(tracker.ActiveHwnd);
                        // Use list-based search when the adapter provides items;
                        // fall back to streaming search when the list is empty.
                        if (listItems.Any())
                        {
                            PerformInlineListProviderSearch(query, adapter, tracker.ActiveHwnd, listItems, searchVersion, onResultsUpdated, token);
                            return;
                        }
                    }

                    bool isExplorer = tracker.IsActiveWindowExplorer;
                    string? scope = isExplorer ? searchScope : null;
                    string? contextDirectory = isInlineSearchContext
                        ? (!string.IsNullOrWhiteSpace(searchScope) ? searchScope : tracker.ActivePath ?? tracker.LastActiveExplorerPath)
                        : tracker.LastActiveExplorerPath;

                    PerformStreamingSearch(query, scope, contextDirectory, isInlineSearchContext, searchVersion, onResultsUpdated, onServiceUnavailable, token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Logger.Log($"[SearchExecutionEngine] PerformSearch failed: {ex}", SwiftList.Core.LogLevel.Error);
                }
                finally
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
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


        private void PerformStreamingSearch(
            string query,
            string? searchScope,
            string? contextDirectory,
            bool isInlineSearchContext,
            int searchVersion,
            Action<List<AppSearchResult>, string, bool> onResultsUpdated,
            Action onServiceUnavailable,
            CancellationToken token)
        {
            var streamedResponse = new SearchResponse();
            object responseLock = new();
            int streamedCount = 0;
            int hasRenderedFirstBatch = 0;

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
                            AppResults = new List<SearchResult>(streamedResponse.AppResults),
                            FileResults = new List<SearchResult>(streamedResponse.FileResults)
                        };
                    }

                    var uiResults = SearchResultMapper.BuildQuickResults(snapshot, query, searchScope, contextDirectory, isInlineSearchContext);
                    if (final && uiResults.Count == 0)
                        uiResults.Add(SearchResultMapper.CreateNoResultsResult(query));

                    string statusText = "";
                    if (uiResults.Count > 0)
                        statusText = SearchResultMapper.FormatSearchStatus(snapshot.AppResults.Count, snapshot.FileResults.Count);
                    else if (final)
                        statusText = "No matching results";

                    onResultsUpdated(uiResults, statusText, final);
                }

                if (final)
                    System.Windows.Application.Current.Dispatcher.Invoke(new Action(ApplySnapshot));
                else
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(ApplySnapshot));
            }

            bool ok = _searchService.SearchStreaming(query, 51, 51, searchScope, (result, isApplication) =>
            {
                token.ThrowIfCancellationRequested();
                lock (responseLock)
                {
                    if (isApplication)
                        streamedResponse.AppResults.Add(result);
                    else
                        streamedResponse.FileResults.Add(result);

                    streamedCount++;
                }

                if (Volatile.Read(ref hasRenderedFirstBatch) == 0 && Volatile.Read(ref streamedCount) < 9)
                    return;

                if (Interlocked.CompareExchange(ref hasRenderedFirstBatch, 1, 0) == 0)
                {
                    RenderSnapshot(final: false);
                }
            }, token);

            if (!ok)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        onServiceUnavailable();
                    }
                }));
                return;
            }

            token.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref hasRenderedFirstBatch, 1);
            RenderSnapshot(final: true);
        }

        private void PerformInlineListProviderSearch(
            string query,
            SwiftList.PluginSdk.IInlineSearchAdapter adapter,
            IntPtr targetHwnd,
            System.Collections.Generic.IEnumerable<string> rawItems,
            int searchVersion,
            Action<List<AppSearchResult>, string, bool> onResultsUpdated,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var uiResults = new List<AppSearchResult>();

            try
            {
                int index = 0;
                foreach (var item in rawItems)
                {
                    if (string.IsNullOrWhiteSpace(item))
                        continue;

                    bool isFullPath = Path.IsPathRooted(item);
                    string displayName = isFullPath
                        ? Path.GetFileName(item.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                        : item;
                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = item;

                    if (displayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        AppSearchResult result;
                        if (isFullPath)
                        {
                            bool isDir = Directory.Exists(item);
                            result = new AppSearchResult
                            {
                                Name = displayName,
                                FullPath = item,
                                ParentDir = string.Empty,
                                ContextDirectory = isDir ? item : (Path.GetDirectoryName(item) ?? string.Empty),
                                IsDir = isDir,
                                Drive = Path.GetPathRoot(item)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar) ?? string.Empty,
                                ResultKind = "File",
                                Index = index,
                                SearchQuery = query
                            };
                        }
                        else
                        {
                            result = new AppSearchResult
                            {
                                Name = displayName,
                                FullPath = item,
                                ResultKind = "InstantResult",
                                Index = index,
                                SearchQuery = query
                            };
                        }
                        uiResults.Add(result);
                        index++;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchExecutionEngine] ListProvider search error: {ex.Message}", LogLevel.Error);
            }

            if (uiResults.Count == 0)
            {
                uiResults.Add(SearchResultMapper.CreateNoResultsResult(query));
            }

            System.Windows.Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                if (searchVersion != Volatile.Read(ref _searchVersion) || token.IsCancellationRequested)
                    return;

                string statusText = uiResults.Count == 1 && uiResults[0].IsEmptyResult
                    ? "No matching results"
                    : string.Format(TranslationManager.Instance["Search_StatsFilesOnly"], uiResults.Count);

                onResultsUpdated(uiResults, statusText, true);
            }));
        }


        public void CancelPendingSearch()
        {
            lock (_searchCtsLock)
            {
                if (_searchCts != null)
                {
                    _searchCts.Cancel();
                    _searchCts.Dispose();
                    _searchCts = null;
                }
            }
        }

        public void Dispose()
        {
            CancelPendingSearch();
            _debounceCts?.Dispose();
        }
    }
}
