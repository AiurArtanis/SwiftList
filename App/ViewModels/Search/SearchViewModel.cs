using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using SwiftList.Core;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Search.Dispatch;
using SwiftList.App.ViewModels.Service;

namespace SwiftList.App.ViewModels.Search;

public class SearchViewModel : ViewModelBase, IDisposable
{
    internal const int FullSearchFileLimit = 1000;
    internal const int FullSearchAppLimit = 0;

    private readonly SearchService _searchService;
    private readonly SearchExecutionEngine _searchEngine;
    private readonly SearchServiceStatusViewModel _serviceStatus;
    private readonly SearchQueryDispatchController _dispatcher;

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

        _dispatcher = new SearchQueryDispatchController(
            _searchEngine,
            _serviceStatus,
            getAllResults: () => _allResults,
            setAllResults: v => _allResults = v,
            setIsSearching: v => IsSearching = v,
            setLoadingPanelVisibility: v => LoadingPanelVisibility = v,
            setIsSearchBoxEnabled: v => IsSearchBoxEnabled = v,
            applyFiltersAndRender: ApplyFiltersAndRender);

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
                    _dispatcher.PerformSearch(value);
                }
                else
                {
                    _dispatcher.OnAdvancedQueryChanged(value);
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

    private string _currentSortColumn = string.Empty;
    private bool _isSortAscending = true;

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
            RenderFinal,
            v => IsSearching = v);
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

    internal void PerformSearch(string query) => _dispatcher.PerformSearch(query);

    public void Dispose()
    {
        _searchEngine.Dispose();
        _serviceStatus.Dispose();
        _searchService.Dispose();
    }
}
