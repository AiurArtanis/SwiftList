using System.Windows;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Search;

// ==========================================
// Typing Debounce & Search logic
// ==========================================
public partial class SearchViewModel
{
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
                // Token providers (e.g. the built-in ":[SCMA]"/".ext"/"!expr" sort+filter+exclude
                // plugin) render via a follow-up ApplyFiltersAndRender inside
                // RefreshAfterTokenDispatchAsync instead of the call below -- a provider with no
                // genuine async work (a plain filter/exclude, no metadata fetch) resolves its
                // already-completed Task inline, so RefreshAfterTokenDispatchAsync can run to
                // completion synchronously right here; rendering the raw (pre-token) results below
                // would then immediately clobber its filtered result with the unfiltered one.
                if (_queryTokens.Count > 0)
                    _ = RefreshAfterTokenDispatchAsync(filteredResults, _queryTokens);
                else
                    ApplyFiltersAndRender();
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
}
