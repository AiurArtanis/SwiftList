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

        TranslationManager.Instance.PropertyChanged += OnTranslationsChanged;
    }

    private void OnTranslationsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "Item[]")
        {
            OnPropertyChanged(nameof(WindowTitle));
            // "N results" was formatted once with the old language's template string and never
            // recomputed until the next search/filter/sort -- refresh it here too so it isn't stuck
            // showing a stale language until the user happens to trigger one of those.
            ResultCountText = string.Format(TranslationManager.Instance["Search_Total"], FilteredResults.Count);
            RefreshDynamicSidebarLabels();
        }
    }

    // DynamicSidebarGroups/their Items were built once in the constructor from each provider's
    // GetFilterGroups(), which resolves its translated Header/DisplayName text at that single call --
    // stale after a language switch otherwise. SidebarFilterGroup has no stable ID (see
    // PluginSdk\Abstractions\Plugins\ISidebarFilterProvider.cs), so this re-fetches the same providers in
    // the same order and correlates purely by position; if a provider's group/item count changed since
    // construction (a plugin was enabled/disabled mid-session), that group is skipped rather than risk
    // relabeling the wrong entry -- rebuilding DynamicSidebarGroups from scratch would also reset the
    // user's current filter selection, which a language switch shouldn't do.
    private void RefreshDynamicSidebarLabels()
    {
        var freshGroups = PluginManager.Instance.SidebarFilterProviders
            .OrderBy(p => p.SortOrder)
            .SelectMany(p => p.GetFilterGroups())
            .ToList();

        if (freshGroups.Count != DynamicSidebarGroups.Count)
            return;

        for (var i = 0; i < DynamicSidebarGroups.Count; i++)
        {
            var vm = DynamicSidebarGroups[i];
            var fresh = freshGroups[i];
            vm.UpdateHeader(fresh.Header);

            if (fresh.Items.Count != vm.Items.Count)
                continue;

            for (var j = 0; j < vm.Items.Count; j++)
                vm.Items[j].UpdateDisplayName(fresh.Items[j].DisplayName);
        }
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
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    // "<keyword> - <app title>" while there's a query, falling back to the plain translated title once
    // it's cleared -- lets the taskbar/Alt+Tab entry identify which search this window is showing.
    // Re-raised on AdvancedQuery changes above and on translation reload below (OnPropertyChanged("Item[]")
    // is TranslationManager's own convention for "every indexer-bound string may have changed").
    public string WindowTitle => string.IsNullOrWhiteSpace(AdvancedQuery)
        ? TranslationManager.Instance["Search_Title"]
        : $"{AdvancedQuery} - {TranslationManager.Instance["Search_Title"]}";

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
            .Select(g => g.CombinedPredicate)
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
        TranslationManager.Instance.PropertyChanged -= OnTranslationsChanged;
        _searchEngine.Dispose();
        _serviceStatus.Dispose();
        _searchService.Dispose();
    }
}
