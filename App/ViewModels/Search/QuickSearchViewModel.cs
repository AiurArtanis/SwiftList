using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using SwiftList.Core;
using SwiftList.App.ViewModels.Service;

namespace SwiftList.App.ViewModels.Search;

public class QuickSearchViewModel : ViewModelBase, IDisposable
{
    private readonly SearchService _searchService;

    public QuickSearchViewModel()
    {
        // Scale result rows/fonts/icons proportionally to the configured search box height.
        Services.UiMetrics.ApplyScaleFromSettings();

        _searchService = new SearchService();
        Search = new SearchExecutionViewModel(this, _searchService);
        Monitor = new ServiceMonitorViewModel(this, _searchService);

        // Forward property changed notifications from sub-ViewModels
        Search.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(SelectedResult))
            {
                OnPropertyChanged(nameof(PathPreviewVisibility));
            }
        };
        Monitor.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);

        // The very first startup-panel activation can race a cold service (e.g. RecentFilesTabSource's
        // IPC round trip) into returning early/incomplete data while it's still spinning up, leaving
        // stale tabs with an empty results area -- and IsServiceConnected defaults to true (optimistic,
        // not confirmed), so a normal cold start where the first ping just succeeds never raises a
        // PropertyChanged for it to react to. ServiceBecameReachable fires unconditionally the moment a
        // ping first actually succeeds, so re-running the empty-state fetch here (a no-op if the box
        // isn't empty) settles the panel onto what it should have shown once the service is genuinely
        // there, instead of staying stuck on whatever the cold-start attempt produced.
        Monitor.ServiceBecameReachable += () => Search.RefreshEmptyState();
    }

    public SearchExecutionViewModel Search { get; }
    public ServiceMonitorViewModel Monitor { get; }

    // ==========================================
    // Delegated Properties for UI Bindings
    // ==========================================

    public ObservableCollection<AppSearchResult> Results => Search.Results;

    public AppSearchResult? SelectedResult
    {
        get => Search.SelectedResult;
        set => Search.SelectedResult = value;
    }

    public Visibility PathPreviewVisibility
    {
        get
        {
            if (IsInlineSearchContext &&
                SelectedResult != null &&
                !SelectedResult.IsEmptyResult &&
                !SelectedResult.IsSearchSectionHeader &&
                !SelectedResult.IsListItem &&
                !SelectedResult.IsPluginSearchAction &&
                !SelectedResult.IsInstantResult &&
                !string.IsNullOrEmpty(SelectedResult.FullPath))
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }
    }

    public string? SearchScope
    {
        get => Search.SearchScope;
        set => Search.SearchScope = value;
    }

    public bool IsInlineSearchContext
    {
        get => Search.IsInlineSearchContext;
        set => Search.IsInlineSearchContext = value;
    }

    public string SearchQuery
    {
        get => Search.SearchQuery;
        set => Search.SearchQuery = value;
    }

    public bool IsIndexReady => Monitor.IsIndexReady;

    public bool IsServiceConnected => Monitor.IsServiceConnected;

    public string StatusText => Monitor.StatusText;

    public Visibility ErrorIconVisibility => Monitor.ErrorIconVisibility;

    public bool IsSearching => Search.IsSearching;

    public bool IsResultsListEnabled => Search.IsResultsListEnabled;

    public Visibility ResultsPanelVisibility
    {
        get => Search.ResultsPanelVisibility;
        set => Search.ResultsPanelVisibility = value;
    }

    public Visibility ResultsSeparatorVisibility
    {
        get => Search.ResultsSeparatorVisibility;
        set => Search.ResultsSeparatorVisibility = value;
    }

    public ObservableCollection<StartupPanelTabViewModel> StartupPanelTabs => Search.StartupPanelTabs;

    public Visibility StartupPanelVisibility => Search.StartupPanelVisibility;

    public Visibility StatusBarVisibility => Monitor.StatusBarVisibility;

    public Visibility LoadingPanelVisibility => Monitor.LoadingPanelVisibility;

    public Visibility ProgressBarVisibility => Monitor.ProgressBarVisibility;

    public Visibility InstallButtonVisibility => Monitor.InstallButtonVisibility;

    public string LoadingTitle => Monitor.LoadingTitle;

    public string LoadingStats => Monitor.LoadingStats;

    public double LoadingProgress
    {
        get => Monitor.LoadingProgress;
        set => Monitor.LoadingProgress = value;
    }

    public bool IsProgressIndeterminate => Monitor.IsProgressIndeterminate;

    public ICommand InstallServiceCommand => Monitor.InstallServiceCommand;

    // ==========================================
    // Delegated Operations
    // ==========================================

    public void TriggerIndexBuild(bool forceRebuild = false) => Monitor.TriggerIndexBuild(forceRebuild);
    public void EnsureServiceMonitoringActive() => Monitor.EnsureServiceMonitoringActive();
    public void RefreshEmptyState() => Search.RefreshEmptyState();

    public CornerRadius WindowCornerRadius => new(UserSettings.Load().SearchWindow.CornerRadius);
    public CornerRadius InnerCornerRadius => new(Math.Max(0, UserSettings.Load().SearchWindow.CornerRadius - 1));
    public double SearchBarWidth => UserSettings.Load().SearchWindow.SearchBarWidth;
    public double SearchBarHeight => UserSettings.Load().SearchWindow.SearchBarHeight;

    public void RefreshLayoutSettings()
    {
        Services.UiMetrics.ApplyScaleFromSettings();
        OnPropertyChanged(nameof(WindowCornerRadius));
        OnPropertyChanged(nameof(InnerCornerRadius));
        OnPropertyChanged(nameof(SearchBarWidth));
        OnPropertyChanged(nameof(SearchBarHeight));
    }

    public void Dispose()
    {
        Monitor.StopStatusTimer();
        _searchService.Dispose();
        Search.Dispose();
    }
}
