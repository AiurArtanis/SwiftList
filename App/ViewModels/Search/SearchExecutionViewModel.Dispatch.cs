using System.Windows;
using SwiftList.Core;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Search;

public partial class SearchExecutionViewModel
{
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

        // Token providers (e.g. the built-in ":[SCMA]"/".ext"/"!expr" sort+filter+exclude plugin)
        // render via a follow-up ReplaceResults inside DispatchTokensAsync instead of the raw
        // ReplaceResults below -- a provider with no genuine async work (a plain filter/exclude, no
        // metadata fetch) resolves its already-completed Task inline, so DispatchTokensAsync can run
        // to completion synchronously right here; falling through to the raw ReplaceResults(uiResults)
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
            ReplaceResults(uiResults);
        }
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

        // A filter/exclude token can legitimately drop every ordinary result -- this window has no
        // separate "no results" hint of its own (unlike the full search window), it renders the
        // synthetic "Empty" row inline, so add one back once there's nothing left to show.
        if (dispatched.Count == 0)
            dispatched.Add(SearchResultMapper.CreateNoResultsResult(query));
        ReplaceResults(dispatched);
    }
}
