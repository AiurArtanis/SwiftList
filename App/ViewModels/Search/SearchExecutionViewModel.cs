using System.IO;
using System.Windows;
using SwiftList.Core;
using SwiftList.App.Services;
using SwiftList.App.Helpers;

namespace SwiftList.App.ViewModels.Search;

public class SearchExecutionViewModel : ViewModelBase, IDisposable
{
    private readonly QuickSearchViewModel _mainVm;
    private readonly SearchExecutionEngine _engine;

    private string _searchQuery = null!;
    private bool _isSearching;
    private bool _isResultsListEnabled = true;
    private AppSearchResult? _selectedResult;

    // UI Panel Visibilities
    private Visibility _resultsPanelVisibility = Visibility.Collapsed;
    private Visibility _resultsSeparatorVisibility = Visibility.Collapsed;
    private string? _searchScope;
    private bool _isInlineSearchContext;
    private readonly System.Windows.Threading.DispatcherTimer _providerLoadedRefreshTimer;

    public SearchExecutionViewModel(QuickSearchViewModel mainVm, SearchService searchService)
    {
        _mainVm = mainVm;
        _engine = new SearchExecutionEngine(searchService);
        Results = new ObservableRangeCollection<AppSearchResult>();

        // Coalesce multiple providers finishing their (background, unawaited) load in quick succession --
        // e.g. right after app startup -- into a single re-run of the current query, instead of one
        // re-run per provider.
        _providerLoadedRefreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _providerLoadedRefreshTimer.Tick += (s, e) =>
        {
            _providerLoadedRefreshTimer.Stop();
            if (!IsActionsMode && !string.IsNullOrWhiteSpace(_searchQuery))
                DispatchSearch(_searchQuery);
        };
        SearchableItemMapper.ProviderLoaded += OnSearchableItemProviderLoaded;
    }

    // Raised from a background thread (see SearchableItemMapper.ProviderLoaded) whenever a searchable-item
    // provider finishes loading. A query issued before that point silently missed that provider's items
    // (AddSearchableItemResults skips providers that aren't cached yet), so re-run the current query to let
    // those items stream in -- ReplaceResults reconciles in place, so this doesn't reset/flicker the list.
    private void OnSearchableItemProviderLoaded() => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
    {
        _providerLoadedRefreshTimer.Stop();
        _providerLoadedRefreshTimer.Start();
    }));

    public ObservableRangeCollection<AppSearchResult> Results { get; }

    public AppSearchResult? SelectedResult
    {
        get => _selectedResult;
        set => SetProperty(ref _selectedResult, value);
    }

    public string? SearchScope
    {
        get => _searchScope;
        set => SetProperty(ref _searchScope, value);
    }

    public bool IsInlineSearchContext
    {
        get => _isInlineSearchContext;
        set => SetProperty(ref _isInlineSearchContext, value);
    }

    private bool _isActionsMode;
    public bool IsActionsMode
    {
        get => _isActionsMode;
        set => SetProperty(ref _isActionsMode, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                if (IsActionsMode)
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(value))
                {
                    _engine.CancelPendingSearch();
                    PerformSearch(value);
                }
                else
                {
                    DispatchSearch(value);
                }
            }
        }
    }

    private void DispatchSearch(string value) => _engine.QueueSearch(
        value,
        SearchScope,
        IsInlineSearchContext,
        fileLimit: 51,
        appLimit: 51,
        resultMapper: (resp, contextDir) => SearchResultMapper.BuildQuickResults(resp, value, IsInlineSearchContext ? null : SearchScope, contextDir, IsInlineSearchContext),
        state => IsSearching = state,
        (results, status, final) => ApplySearchResults(value, results, status, final),
        HandleLocalServiceUnavailable,
        shouldEmitInstantResults: () => Results.Count == 0
    );

    public bool IsSearching
    {
        get => _isSearching;
        set
        {
            if (SetProperty(ref _isSearching, value))
            {
                // Keep list enabled during search to prevent Win32 system disabled theme flash and allow immediate navigation
                // IsResultsListEnabled = !value;
            }
        }
    }

    public bool IsResultsListEnabled
    {
        get => _isResultsListEnabled;
        set => SetProperty(ref _isResultsListEnabled, value);
    }

    public Visibility ResultsPanelVisibility
    {
        get => _resultsPanelVisibility;
        set => SetProperty(ref _resultsPanelVisibility, value);
    }

    public Visibility ResultsSeparatorVisibility
    {
        get => _resultsSeparatorVisibility;
        set => SetProperty(ref _resultsSeparatorVisibility, value);
    }

    public void PerformSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _engine.CancelPendingSearch();
            IsSearching = false;
            ReplaceResults(Array.Empty<AppSearchResult>());

            var tracker = InlineSearchManager.Instance.ExplorerTracker;
            var lastPath = tracker.LastActiveExplorerPath;
            var isDialog = tracker.IsActiveWindowDialog;
            var dirExists = !string.IsNullOrEmpty(lastPath) &&
                            (Directory.Exists(lastPath) ||
                             (lastPath.Length >= 3 && lastPath[1] == ':' && lastPath[2] == '\\' && char.IsLetter(lastPath[0])));

            var searchScopeTrimmed = SearchScope?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var lastPathTrimmed = lastPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var isSamePath = string.Equals(searchScopeTrimmed, lastPathTrimmed, StringComparison.OrdinalIgnoreCase);

            Logger.Log($"[Diagnosis] SearchScope='{SearchScope}', isDialog={isDialog}, lastPath='{lastPath}', dirExists={dirExists}, isSamePath={isSamePath}", LogLevel.Debug);

            if (IsInlineSearchContext && isDialog && dirExists && !string.IsNullOrEmpty(lastPath) && (string.IsNullOrEmpty(SearchScope) || !isSamePath))
            {
                string? targetName = null;
                var className = tracker.LastActiveExplorerClassName;
                if (className != null)
                {
                    var collectors = PluginSdk.Registries.ActivePathCollectorRegistry.GetCollectors();
                    foreach (var collector in collectors)
                    {
                        if (collector.CanHandle(className))
                        {
                            targetName = collector.TargetName;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(targetName))
                {
                    var displayName = targetName;

                    ReplaceResults(new[]
                    {
                        new AppSearchResult
                        {
                            Name = displayName,
                            FullPath = lastPath,
                            ParentDir = lastPath,
                            IsDir = true,
                            Drive = string.Empty,
                            ResultKind = "JumpToExplorerPath",
                            Index = 0,
                            SearchQuery = string.Empty
                        }
                    });

                    ResultsPanelVisibility = Visibility.Visible;
                    ResultsSeparatorVisibility = Visibility.Visible;
                }
            }
            else
            {
                ResultsPanelVisibility = Visibility.Collapsed;
                ResultsSeparatorVisibility = Visibility.Collapsed;
            }

            if (_mainVm.Monitor.IsIndexReady)
            {
                _mainVm.Monitor.StatusBarVisibility = Visibility.Visible;
                _mainVm.Monitor.StatusText = string.Format(TranslationManager.Instance["Service_IndexedTemplate"], _mainVm.Monitor.GetStatusFiles(), _mainVm.Monitor.GetStatusDirs());
            }
            else
            {
                _mainVm.Monitor.StatusBarVisibility = Visibility.Collapsed;
            }
            return;
        }

        _engine.PerformSearch(
            query,
            SearchScope,
            IsInlineSearchContext,
            fileLimit: 51,
            appLimit: 51,
            resultMapper: (resp, contextDir) => SearchResultMapper.BuildQuickResults(resp, query, IsInlineSearchContext ? null : SearchScope, contextDir, IsInlineSearchContext),
            state => IsSearching = state,
            (results, status, final) => ApplySearchResults(query, results, status, final),
            HandleLocalServiceUnavailable,
            shouldEmitInstantResults: () => Results.Count == 0
        );
    }

    private void HandleLocalServiceUnavailable() => _mainVm.TriggerIndexBuild();

    private void ApplySearchResults(string query, List<AppSearchResult> uiResults, string statusText, bool final)
    {
        if (SearchQuery != query)
            return;

        // ReplaceResults reconciles row-by-row and no-ops when nothing changed, so no pre-check needed.
        ReplaceResults(uiResults);

        var hasResults = uiResults.Count > 0;
        ResultsPanelVisibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        ResultsSeparatorVisibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        _mainVm.Monitor.StatusBarVisibility = Visibility.Visible;
        _mainVm.Monitor.StatusText = statusText;
    }

    private static bool ItemsEqual(AppSearchResult a, AppSearchResult b) =>
        string.Equals(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Name, b.Name, StringComparison.Ordinal) &&
        string.Equals(a.ResultKind, b.ResultKind, StringComparison.Ordinal) &&
        string.Equals(a.SearchQuery, b.SearchQuery, StringComparison.Ordinal);

    private void ReplaceResults(IEnumerable<AppSearchResult> results)
    {
        var list = results as List<AppSearchResult> ?? new List<AppSearchResult>(results);

        // Reconcile row-by-row instead of a full Clear+Add reset: only changed rows are replaced in
        // place (recycling ListBox reuses containers) and the tail is appended/trimmed, so the list
        // is never torn down and rebuilt from the top — which is what caused the flicker.
        Results.ReconcileTo(list, ItemsEqual);

        // Keep the current selection if it survived the update; only re-select when it's gone or
        // no longer selectable, so streaming updates don't yank the highlight back to the top.
        if (SelectedResult != null && Results.Contains(SelectedResult)
            && !SelectedResult.IsEmptyResult && !SelectedResult.IsSearchSectionHeader)
            return;

        AppSearchResult? firstSelectable = null;
        foreach (var result in list)
        {
            if (!result.IsEmptyResult && !result.IsSearchSectionHeader)
            {
                firstSelectable = result;
                break;
            }
        }

        SelectedResult = firstSelectable;
    }

    public void CancelPendingSearch() => _engine.CancelPendingSearch();

    public void Dispose()
    {
        SearchableItemMapper.ProviderLoaded -= OnSearchableItemProviderLoaded;
        _providerLoadedRefreshTimer.Stop();
        _engine.Dispose();
    }
}
