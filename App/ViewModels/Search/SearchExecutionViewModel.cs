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

    public SearchExecutionViewModel(QuickSearchViewModel mainVm, SearchService searchService)
    {
        _mainVm = mainVm;
        _engine = new SearchExecutionEngine(searchService);
        Results = new ObservableRangeCollection<AppSearchResult>();
    }

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

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _engine.CancelPendingSearch();
                    PerformSearch(value);
                }
                else
                {
                    _engine.QueueSearch(
                        value,
                        SearchScope,
                        IsInlineSearchContext,
                        state => IsSearching = state,
                        (results, status, final) => ApplySearchResults(value, results, status, final),
                        OnServiceUnavailable
                    );
                }
            }
        }
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

            if (isDialog && dirExists && !string.IsNullOrEmpty(lastPath) && (string.IsNullOrEmpty(SearchScope) || !isSamePath))
            {
                string? targetName = null;
                var className = tracker.LastActiveExplorerClassName;
                if (className != null)
                {
                    var collectors = PluginSdk.ActivePathCollectorRegistry.GetCollectors();
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
                    var format = TranslationManager.Instance["Search_JumpToExplorer"];
                    var displayName = string.Format(format, targetName);

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
            state => IsSearching = state,
            (results, status, final) => ApplySearchResults(query, results, status, final),
            OnServiceUnavailable
        );
    }

    private void ApplySearchResults(string query, List<AppSearchResult> uiResults, string statusText, bool final)
    {
        if (SearchQuery != query)
            return;

        if (!AreSameResults(Results, uiResults))
            ReplaceResults(uiResults);

        var hasResults = uiResults.Count > 0;
        ResultsPanelVisibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        ResultsSeparatorVisibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        _mainVm.Monitor.StatusBarVisibility = Visibility.Visible;
        _mainVm.Monitor.StatusText = statusText;
    }

    private void OnServiceUnavailable()
    {
        _mainVm.Monitor.SetOfflineState();
        ReplaceResults(Array.Empty<AppSearchResult>());
        ResultsPanelVisibility = Visibility.Visible;
        ResultsSeparatorVisibility = Visibility.Visible;
    }

    private static bool AreSameResults(IReadOnlyList<AppSearchResult> current, IReadOnlyList<AppSearchResult> next)
    {
        if (current.Count != next.Count)
            return false;

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(current[i].FullPath, next[i].FullPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(current[i].Name, next[i].Name, StringComparison.Ordinal) ||
                !string.Equals(current[i].ResultKind, next[i].ResultKind, StringComparison.Ordinal) ||
                !string.Equals(current[i].SearchQuery, next[i].SearchQuery, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void ReplaceResults(IEnumerable<AppSearchResult> results)
    {
        AppSearchResult? firstSelectable = null;
        var list = results as List<AppSearchResult> ?? new List<AppSearchResult>(results);
        Results.ReplaceRange(list);

        foreach (var result in list)
        {
            if (firstSelectable == null && !result.IsEmptyResult && !result.IsSearchSectionHeader)
            {
                firstSelectable = result;
                break;
            }
        }

        SelectedResult = firstSelectable;
    }

    public void CancelPendingSearch() => _engine.CancelPendingSearch();

    public void Dispose() => _engine.Dispose();
}
