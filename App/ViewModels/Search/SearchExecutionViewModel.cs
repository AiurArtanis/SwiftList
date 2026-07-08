using System.Collections.ObjectModel;
using System.Windows;
using SwiftList.Core;
using SwiftList.App.Services;
using SwiftList.App.Helpers;

namespace SwiftList.App.ViewModels.Search;

public class SearchExecutionViewModel : ViewModelBase, IDisposable
{
    private readonly QuickSearchViewModel _mainVm;
    private readonly SearchExecutionEngine _engine;
    private readonly StartupPanelController _startupPanel;

    private string _searchQuery = null!;
    private IReadOnlyList<string> _queryTokens = Array.Empty<string>();
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

        _startupPanel = new StartupPanelController(searchService, ReplaceResults);
        _startupPanel.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(StartupPanelController.Visibility)) OnPropertyChanged(nameof(StartupPanelVisibility)); };

        // Coalesce multiple providers finishing their (background, unawaited) load in quick succession
        // (e.g. right after app startup) into a single re-run of the current query, not one per provider.
        _providerLoadedRefreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
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

    private void DispatchSearch(string value)
    {
        var cleanQuery = SearchQuerySortParser.Strip(value, out var tokens);
        _queryTokens = tokens;

        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            _engine.CancelPendingSearch();
            PerformSearch(string.Empty);
            return;
        }

        _engine.QueueSearch(
            cleanQuery,
            SearchScope,
            IsInlineSearchContext,
            fileLimit: 51,
            appLimit: 51,
            resultMapper: (resp, contextDir) => SearchResultMapper.BuildQuickResults(resp, cleanQuery, IsInlineSearchContext ? null : SearchScope, contextDir, IsInlineSearchContext),
            state => IsSearching = state,
            (results, status, final) => ApplySearchResults(value, results, status, final),
            HandleLocalServiceUnavailable,
            shouldEmitInstantResults: () => Results.Count == 0
        );
    }

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

    // "初始面板" (Startup Panel): shown above the results in the quick popup only, see StartupPanelController.
    public ObservableCollection<StartupPanelTabViewModel> StartupPanelTabs => _startupPanel.Tabs;
    public Visibility StartupPanelVisibility => _startupPanel.Visibility;
    public void SelectNextStartupPanelTab() => _startupPanel.SelectNextTab();
    public void SelectPreviousStartupPanelTab() => _startupPanel.SelectPreviousTab();

    // SearchQuery's setter only re-runs PerformSearch when the value changes, so re-showing the window
    // while the box stays empty wouldn't otherwise notice a new file, or a tab re-enabled in Settings.
    public void RefreshEmptyState()
    {
        if (string.IsNullOrWhiteSpace(_searchQuery))
            PerformSearch(_searchQuery);
    }

    public void PerformSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _engine.CancelPendingSearch();
            IsSearching = false;
            ReplaceResults(Array.Empty<AppSearchResult>());

            var suggestion = ExplorerJumpSuggestionHelper.TryBuildSuggestion(IsInlineSearchContext, SearchScope);
            if (suggestion != null)
            {
                _startupPanel.Deactivate();
                ReplaceResults(new[] { suggestion });
                ResultsPanelVisibility = Visibility.Visible;
                ResultsSeparatorVisibility = Visibility.Visible;
            }
            else
            {
                ResultsPanelVisibility = Visibility.Collapsed;
                ResultsSeparatorVisibility = Visibility.Collapsed;
                if (!IsInlineSearchContext)
                    _ = ActivateStartupPanelAsync();
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

        var cleanQuery = SearchQuerySortParser.Strip(query, out var tokens);
        _queryTokens = tokens;

        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            PerformSearch(string.Empty);
            return;
        }

        _engine.PerformSearch(
            cleanQuery,
            SearchScope,
            IsInlineSearchContext,
            fileLimit: 51,
            appLimit: 51,
            resultMapper: (resp, contextDir) => SearchResultMapper.BuildQuickResults(resp, cleanQuery, IsInlineSearchContext ? null : SearchScope, contextDir, IsInlineSearchContext),
            state => IsSearching = state,
            (results, status, final) => ApplySearchResults(query, results, status, final),
            HandleLocalServiceUnavailable,
            shouldEmitInstantResults: () => Results.Count == 0
        );
    }

    private void HandleLocalServiceUnavailable() => _mainVm.TriggerIndexBuild();

    private async Task ActivateStartupPanelAsync()
    {
        var shown = await _startupPanel.TryActivateAsync();
        if (!shown || !string.IsNullOrWhiteSpace(_searchQuery))
            return; // a real query started while the fetch was in flight; ApplySearchResults handled visibility

        ResultsPanelVisibility = Visibility.Visible;
        ResultsSeparatorVisibility = Visibility.Visible;
    }

    private void ApplySearchResults(string query, List<AppSearchResult> uiResults, string statusText, bool final)
    {
        if (SearchQuery != query)
            return;

        // Token providers (e.g. the built-in ":[SCMA]"/".ext" sort+filter plugin) run async -- they
        // may fetch metadata over IPC -- so their effect lands via a follow-up ReplaceResults rather
        // than this render. Skipped if a newer search has since taken over.
        if (_queryTokens.Count > 0)
        {
            var tokensSnapshot = _queryTokens;
            var resultsSnapshot = uiResults;
            _ = DispatchTokensAsync(query, resultsSnapshot, tokensSnapshot);
        }

        // ReplaceResults reconciles row-by-row and no-ops when nothing changed, so no pre-check needed.
        ReplaceResults(uiResults);
        _startupPanel.Deactivate();

        var hasResults = uiResults.Count > 0;
        ResultsPanelVisibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        ResultsSeparatorVisibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        _mainVm.Monitor.StatusBarVisibility = Visibility.Visible;
        _mainVm.Monitor.StatusText = statusText;
    }

    private async Task DispatchTokensAsync(string query, List<AppSearchResult> resultsSnapshot, IReadOnlyList<string> tokensSnapshot)
    {
        var dispatched = await QueryTokenDispatcher.ApplyAsync(resultsSnapshot, tokensSnapshot);
        if (SearchQuery != query || !ReferenceEquals(_queryTokens, tokensSnapshot))
            return;
        ReplaceResults(dispatched);
    }

    private void ReplaceResults(IEnumerable<AppSearchResult> results) =>
        SearchResultsReconciler.Replace(Results, results, SelectedResult, v => SelectedResult = v);

    public void CancelPendingSearch() => _engine.CancelPendingSearch();

    public void Dispose()
    {
        SearchableItemMapper.ProviderLoaded -= OnSearchableItemProviderLoaded;
        _providerLoadedRefreshTimer.Stop();
        _startupPanel.Deactivate();
        _engine.Dispose();
    }
}
