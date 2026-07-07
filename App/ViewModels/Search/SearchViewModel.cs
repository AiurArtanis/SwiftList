using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using SwiftList.Core;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Service;

namespace SwiftList.App.ViewModels.Search;

public class SearchViewModel : ViewModelBase, IDisposable
{
    private const int FullSearchFileLimit = 1000;
    private const int FullSearchAppLimit = 0;

    private readonly SearchService _searchService;
    private readonly SearchExecutionEngine _searchEngine;
    private readonly SearchServiceStatusViewModel _serviceStatus;

    private string _advancedQuery = string.Empty;
    private List<AppSearchResult> _allResults = new();
    private string _resultCountText = "";
    private bool _isSearching;
    private bool _isResultsListEnabled = true;
    // Deliberately does not toggle IsResultsListEnabled while searching -- that used to disable the
    // list mid-search, which caused a Win32 disabled-theme flash and blocked immediate navigation.
    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }

    public bool IsResultsListEnabled
    {
        get => _isResultsListEnabled;
        private set => SetProperty(ref _isResultsListEnabled, value);
    }

    public SearchViewModel(string initialQuery = "")
    {
        _searchService = new SearchService();
        _searchEngine = new SearchExecutionEngine(_searchService);
        FilteredResults = new ObservableRangeCollection<AppSearchResult>();

        _serviceStatus = new SearchServiceStatusViewModel(this, _searchService);
        _serviceStatus.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);

        // Initialize dynamic plugin sidebar groups
        var orderedProviders = PluginManager.Instance.SidebarFilterProviders
            .OrderBy(p => p.SortOrder)
            .ToList();

        foreach (var provider in orderedProviders)
        {
            foreach (var group in provider.GetFilterGroups())
            {
                DynamicSidebarGroups.Add(new DynamicSidebarGroupViewModel(group, this));
            }
        }

        if (DynamicSidebarGroups.Count > 0)
        {
            DynamicSidebarGroups[0].IsFirst = true;
        }

        ResultCountText = string.Format(TranslationManager.Instance["Search_Total"], 0);
        AdvancedQuery = initialQuery;
    }

    // ==========================================
    // Properties
    // ==========================================

    public ObservableRangeCollection<AppSearchResult> FilteredResults { get; }
    public ObservableCollection<DynamicSidebarGroupViewModel> DynamicSidebarGroups { get; } = new();

    public string AdvancedQuery
    {
        get => _advancedQuery;
        set
        {
            if (SetProperty(ref _advancedQuery, value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _searchEngine.CancelPendingSearch();
                    PerformSearch(value);
                }
                else
                {
                    OnAdvancedQueryChanged(value);
                }
                OnPropertyChanged(nameof(ShowWelcomeHint));
                OnPropertyChanged(nameof(ShowNoResultsHint));
            }
        }
    }

    public string ResultCountText
    {
        get => _resultCountText;
        private set => SetProperty(ref _resultCountText, value);
    }

    // ==========================================
    // Service status properties delegation
    // ==========================================

    public bool IsSearchBoxEnabled
    {
        get => _serviceStatus.IsSearchBoxEnabled;
        set => _serviceStatus.IsSearchBoxEnabled = value;
    }

    public bool IsServiceConnected => _serviceStatus.IsServiceConnected;

    public Visibility LoadingPanelVisibility
    {
        get => _serviceStatus.LoadingPanelVisibility;
        internal set => _serviceStatus.LoadingPanelVisibility = value;
    }

    public Visibility ProgressBarVisibility => _serviceStatus.ProgressBarVisibility;
    public bool IsProgressIndeterminate => _serviceStatus.IsProgressIndeterminate;
    public double LoadingProgress
    {
        get => _serviceStatus.LoadingProgress;
        set => _serviceStatus.LoadingProgress = value;
    }
    public Visibility ErrorIconVisibility => _serviceStatus.ErrorIconVisibility;
    public string LoadingTitle => _serviceStatus.LoadingTitle;
    public string LoadingStats => _serviceStatus.LoadingStats;
    public Visibility InstallButtonVisibility => _serviceStatus.InstallButtonVisibility;
    public ICommand InstallServiceCommand => _serviceStatus.InstallServiceCommand;

    // ==========================================
    // Typing Debounce & Search logic
    // ==========================================

    private void OnAdvancedQueryChanged(string query)
    {
        var cleanQuery = SearchQuerySortParser.Strip(query, out var tokens);
        _queryTokens = tokens;

        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            ClearResults();
            return;
        }

        _searchEngine.QueueSearch(
            cleanQuery,
            searchScope: null,
            isInlineSearchContext: false,
            fileLimit: FullSearchFileLimit,
            appLimit: FullSearchAppLimit,
            resultMapper: (fileResults, _) =>
            {
                var results = new List<AppSearchResult>();
                if (fileResults != null)
                {
                    for (var i = 0; i < fileResults.Count; i++)
                    {
                        results.Add(SearchResultMapper.CreateUiResult(fileResults[i], cleanQuery, results.Count, isApplication: false, scope: null));
                    }
                }
                return results;
            },
            searching => IsSearching = searching,
            (results, status, final) =>
            {
                _serviceStatus.ClearReconnectState();
                LoadingPanelVisibility = Visibility.Collapsed;
                IsSearchBoxEnabled = true;
                // This window has its own "no results" hint (ShowNoResultsHint, keyed off an empty
                // FilteredResults) -- the shared engine's synthetic "Empty" placeholder row is meant
                // for the quick/inline windows, which have no such hint and render it inline instead.
                // Left in here, it counts toward FilteredResults.Count and shows up as a real grid row.
                var filteredResults = results.Where(r => !r.IsEmptyResult).ToList();
                _allResults = filteredResults;
                ApplyFiltersAndRender();
                // Token providers (e.g. the built-in ":[SCMA]"/".ext" sort+filter plugin) run async --
                // they may fetch metadata over IPC -- so their effect lands via a follow-up re-render
                // rather than the render just above. Skipped if a newer search has since taken over.
                if (_queryTokens.Count > 0)
                    _ = RefreshAfterTokenDispatchAsync(filteredResults, _queryTokens);
                if (final)
                    IsSearching = false;
            },
            () => _serviceStatus.CheckServiceStatusOnStartup()
        );
    }

    private async Task RefreshAfterTokenDispatchAsync(List<AppSearchResult> resultsSnapshot, IReadOnlyList<string> tokensSnapshot)
    {
        var dispatched = await QueryTokenDispatcher.ApplyAsync(resultsSnapshot, tokensSnapshot);
        if (!ReferenceEquals(_allResults, resultsSnapshot) || !ReferenceEquals(_queryTokens, tokensSnapshot))
            return;
        _allResults = dispatched;
        ApplyFiltersAndRender();
    }

    internal void PerformSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ClearResults();
            return;
        }

        OnAdvancedQueryChanged(query);
    }

    private void ClearResults()
    {
        _searchEngine.CancelPendingSearch();
        IsSearching = false;
        _allResults.Clear();
        ApplyFiltersAndRender();
        LoadingPanelVisibility = Visibility.Collapsed;
    }

    private string _currentSortColumn = string.Empty;
    private bool _isSortAscending = true;
    private IReadOnlyList<string> _queryTokens = Array.Empty<string>();

    public bool IsSortAscending => _isSortAscending;

    public void SortByColumn(string columnHeaderOrId)
    {
        if (_currentSortColumn == columnHeaderOrId)
        {
            _isSortAscending = !_isSortAscending;
        }
        else
        {
            _currentSortColumn = columnHeaderOrId;
            _isSortAscending = true;
        }
        ApplyFiltersAndRender();
    }

    public void OnDynamicFilterChanged() => ApplyFiltersAndRender();

    private readonly DynamicFilterCoordinator _dynamicFilterCoordinator = new();

    private void ApplyFiltersAndRender()
    {
        if (_allResults == null) return;

        var activeFilters = DynamicSidebarGroups
            .Select(g => g.SelectedItem?.FilterPredicate)
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        // Query-token providers (sort/filter/etc) have already been applied to _allResults by the
        // time this runs -- this only handles the column-header sort and dynamic sidebar filters.
        _dynamicFilterCoordinator.Apply(
            _allResults,
            activeFilters,
            results => SearchResultSorter.Sort(results, _currentSortColumn, _isSortAscending).ToList(),
            () => _allResults,
            RenderFinal);
    }

    private void RenderFinal(List<AppSearchResult> finalResults)
    {
        FilteredResults.ReplaceRange(finalResults);
        ResultCountText = string.Format(TranslationManager.Instance["Search_Total"], finalResults.Count);
        OnPropertyChanged(nameof(ShowNoResultsHint));
        OnPropertyChanged(nameof(ShowWelcomeHint));
    }

    private bool _isActionsMode;
    public bool IsActionsMode
    {
        get => _isActionsMode;
        set
        {
            if (SetProperty(ref _isActionsMode, value))
            {
                OnPropertyChanged(nameof(ShowNoResultsHint));
                OnPropertyChanged(nameof(ShowWelcomeHint));
            }
        }
    }

    public bool ShowNoResultsHint => !IsActionsMode && FilteredResults.Count == 0 && !string.IsNullOrWhiteSpace(AdvancedQuery);
    public bool ShowWelcomeHint => !IsActionsMode && string.IsNullOrWhiteSpace(AdvancedQuery);

    public void Dispose()
    {
        _searchEngine.Dispose();
        _serviceStatus.Dispose();
        _searchService.Dispose();
    }
}
