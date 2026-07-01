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
    public bool IsSearching
    {
        get => _isSearching;
        private set
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

    private void OnAdvancedQueryChanged(string query) => _searchEngine.QueueSearch(
            query,
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
                        results.Add(SearchResultMapper.CreateUiResult(fileResults[i], query, results.Count, isApplication: false, scope: null));
                    }
                }
                return results;
            },
            searching => IsSearching = searching,
            (results, status, final) =>
            {
                _serviceStatus.ResetAutoInstallFlag();
                LoadingPanelVisibility = Visibility.Collapsed;
                IsSearchBoxEnabled = true;
                _allResults = results;
                ApplyFiltersAndRender();
                if (final)
                    IsSearching = false;
            },
            () => _serviceStatus.CheckServiceStatusOnStartup()
        );

    internal void PerformSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _searchEngine.CancelPendingSearch();
            IsSearching = false;
            _allResults.Clear();
            ApplyFiltersAndRender();
            LoadingPanelVisibility = Visibility.Collapsed;
            return;
        }

        OnAdvancedQueryChanged(query);
    }

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

    private void ApplyFiltersAndRender()
    {
        if (_allResults == null) return;
        var resultsList = _allResults.AsEnumerable();

        // Apply all active dynamic plugin sidebar filters
        foreach (var group in DynamicSidebarGroups)
        {
            if (group.SelectedItem != null && group.SelectedItem.FilterPredicate != null)
            {
                var predicate = group.SelectedItem.FilterPredicate;
                resultsList = resultsList.Where(res => predicate(res));
            }
        }

        // Apply sorting
        resultsList = SearchResultSorter.Sort(resultsList, _currentSortColumn, _isSortAscending);

        var finalResults = resultsList.ToList();
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
