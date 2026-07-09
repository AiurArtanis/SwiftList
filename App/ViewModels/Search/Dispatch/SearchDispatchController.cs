using System.Windows;
using SwiftList.Core;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Search.Dispatch;

// Owns query-token parsing, dispatching a search (debounced/quick vs. blocking), and rendering the
// resulting rows on behalf of SearchExecutionViewModel -- extracted into its own class (composition,
// not a partial class) purely to keep SearchExecutionViewModel.cs under the repo's per-file line limit.
internal sealed class SearchDispatchController
{
    private readonly SearchExecutionEngine _engine;
    private readonly StartupPanelController _startupPanel;
    private readonly QuickSearchViewModel _mainVm;
    private readonly Func<string?> _getSearchScope;
    private readonly Func<bool> _getIsInlineSearchContext;
    private readonly Func<string> _getSearchQuery;
    private readonly Action<bool> _setIsSearching;
    private readonly Action<Visibility> _setResultsPanelVisibility;
    private readonly Action<Visibility> _setResultsSeparatorVisibility;
    private readonly Action<IEnumerable<AppSearchResult>> _replaceResults;
    private readonly Func<int> _getResultsCount;

    private IReadOnlyList<string> _queryTokens = Array.Empty<string>();

    public SearchDispatchController(
        SearchExecutionEngine engine,
        StartupPanelController startupPanel,
        QuickSearchViewModel mainVm,
        Func<string?> getSearchScope,
        Func<bool> getIsInlineSearchContext,
        Func<string> getSearchQuery,
        Action<bool> setIsSearching,
        Action<Visibility> setResultsPanelVisibility,
        Action<Visibility> setResultsSeparatorVisibility,
        Action<IEnumerable<AppSearchResult>> replaceResults,
        Func<int> getResultsCount)
    {
        _engine = engine;
        _startupPanel = startupPanel;
        _mainVm = mainVm;
        _getSearchScope = getSearchScope;
        _getIsInlineSearchContext = getIsInlineSearchContext;
        _getSearchQuery = getSearchQuery;
        _setIsSearching = setIsSearching;
        _setResultsPanelVisibility = setResultsPanelVisibility;
        _setResultsSeparatorVisibility = setResultsSeparatorVisibility;
        _replaceResults = replaceResults;
        _getResultsCount = getResultsCount;
    }

    public void DispatchSearch(string value)
    {
        var cleanQuery = SearchQuerySortParser.Strip(value, out var tokens);
        _queryTokens = tokens;

        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            _engine.CancelPendingSearch();
            PerformSearch(string.Empty);
            return;
        }

        RunEngineSearch(_engine.QueueSearch, value, cleanQuery);
    }

    // DispatchSearch (debounced) and PerformSearch (blocking) both resolve to the same set of
    // search parameters -- only which SearchExecutionEngine method runs them differs.
    private void RunEngineSearch(
        Action<string, string?, bool, int, int, Func<List<SearchResult>?, string?, List<AppSearchResult>>, Action<bool>, Action<List<AppSearchResult>, string, bool>, Action?, Func<bool>?> engineCall,
        string originalValue,
        string cleanQuery) =>
        engineCall(
            cleanQuery,
            _getSearchScope(),
            _getIsInlineSearchContext(),
            51,
            51,
            (resp, contextDir) => SearchResultMapper.BuildQuickResults(resp, cleanQuery, _getIsInlineSearchContext() ? null : _getSearchScope(), contextDir, _getIsInlineSearchContext()),
            state => _setIsSearching(state),
            (results, status, final) => ApplySearchResults(originalValue, results, status, final),
            HandleLocalServiceUnavailable,
            () => _getResultsCount() == 0
        );

    public void PerformSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _engine.CancelPendingSearch();
            _setIsSearching(false);
            _replaceResults(Array.Empty<AppSearchResult>());

            var suggestion = ExplorerJumpSuggestionHelper.TryBuildSuggestion(_getIsInlineSearchContext(), _getSearchScope());
            if (suggestion != null)
            {
                _startupPanel.Deactivate();
                _replaceResults(new[] { suggestion });
                _setResultsPanelVisibility(Visibility.Visible);
                _setResultsSeparatorVisibility(Visibility.Visible);
            }
            else
            {
                _setResultsPanelVisibility(Visibility.Collapsed);
                _setResultsSeparatorVisibility(Visibility.Collapsed);
                if (!_getIsInlineSearchContext())
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

        RunEngineSearch(_engine.PerformSearch, query, cleanQuery);
    }

    private void HandleLocalServiceUnavailable() => _mainVm.TriggerIndexBuild();

    private async Task ActivateStartupPanelAsync()
    {
        var shown = await _startupPanel.TryActivateAsync();
        if (!shown || !string.IsNullOrWhiteSpace(_getSearchQuery()))
            return; // a real query started while the fetch was in flight; ApplySearchResults handled visibility

        _setResultsPanelVisibility(Visibility.Visible);
        _setResultsSeparatorVisibility(Visibility.Visible);
    }

    private void ApplySearchResults(string query, List<AppSearchResult> uiResults, string statusText, bool final)
    {
        if (_getSearchQuery() != query)
            return;

        // Token providers (e.g. the built-in ":[SCMA]"/".ext"/"::expr" sort+filter+match plugin)
        // render via a follow-up ReplaceResults inside DispatchTokensAsync instead of the raw
        // ReplaceResults below -- a provider with no genuine async work (a plain filter, no metadata
        // fetch) resolves its already-completed Task inline, so DispatchTokensAsync can run to
        // completion synchronously right here; falling through to the raw ReplaceResults(uiResults)
        // below would then immediately clobber its filtered result with the unfiltered one.
        if (_queryTokens.Count > 0)
        {
            var tokensSnapshot = _queryTokens;
            var resultsSnapshot = uiResults;
            _ = DispatchTokensAsync(query, resultsSnapshot, tokensSnapshot);
        }
        else
        {
            // ReplaceResults reconciles row-by-row and no-ops when nothing changed, so no pre-check needed.
            _replaceResults(uiResults);
        }
        _startupPanel.Deactivate();

        var hasResults = uiResults.Count > 0;
        _setResultsPanelVisibility(hasResults ? Visibility.Visible : Visibility.Collapsed);
        _setResultsSeparatorVisibility(hasResults ? Visibility.Visible : Visibility.Collapsed);
        _mainVm.Monitor.StatusBarVisibility = Visibility.Visible;
        _mainVm.Monitor.StatusText = statusText;
    }

    private async Task DispatchTokensAsync(string query, List<AppSearchResult> resultsSnapshot, IReadOnlyList<string> tokensSnapshot)
    {
        var dispatched = await QueryTokenDispatcher.ApplyAsync(resultsSnapshot, tokensSnapshot);
        if (_getSearchQuery() != query || !ReferenceEquals(_queryTokens, tokensSnapshot))
            return;

        // A filter token can legitimately drop every ordinary result -- this window has no separate
        // "no results" hint of its own (unlike the full search window), it renders the synthetic
        // "Empty" row inline, so add one back once there's nothing left to show.
        if (dispatched.Count == 0)
            dispatched.Add(SearchResultMapper.CreateNoResultsResult(query));
        _replaceResults(dispatched);
    }
}
