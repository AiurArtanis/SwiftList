using System.Windows;
using SwiftList.Core;
using SwiftList.App.ViewModels.Service;

using SwiftList.Core.SearchIndex.Query;
using SwiftList.App.ViewModels.Search.Mapping;
namespace SwiftList.App.ViewModels.Search.Dispatch;

// Owns query-token parsing and search dispatch for the full search window's SearchViewModel --
// extracted into its own class (composition, not a partial class) purely to keep SearchViewModel.cs
// under the repo's per-file line limit.
internal sealed class SearchQueryDispatchController
{
    private readonly SearchExecutionEngine _searchEngine;
    private readonly SearchServiceStatusViewModel _serviceStatus;
    private readonly Func<List<AppSearchResult>> _getAllResults;
    private readonly Action<List<AppSearchResult>> _setAllResults;
    private readonly Action<bool> _setIsSearching;
    private readonly Action<Visibility> _setLoadingPanelVisibility;
    private readonly Action<bool> _setIsSearchBoxEnabled;
    private readonly Action _applyFiltersAndRender;

    private IReadOnlyList<string> _queryTokens = Array.Empty<string>();

    public SearchQueryDispatchController(
        SearchExecutionEngine searchEngine,
        SearchServiceStatusViewModel serviceStatus,
        Func<List<AppSearchResult>> getAllResults,
        Action<List<AppSearchResult>> setAllResults,
        Action<bool> setIsSearching,
        Action<Visibility> setLoadingPanelVisibility,
        Action<bool> setIsSearchBoxEnabled,
        Action applyFiltersAndRender)
    {
        _searchEngine = searchEngine;
        _serviceStatus = serviceStatus;
        _getAllResults = getAllResults;
        _setAllResults = setAllResults;
        _setIsSearching = setIsSearching;
        _setLoadingPanelVisibility = setLoadingPanelVisibility;
        _setIsSearchBoxEnabled = setIsSearchBoxEnabled;
        _applyFiltersAndRender = applyFiltersAndRender;
    }

    public void OnAdvancedQueryChanged(string query)
    {
        var strippedTrailing = SearchQuerySortParser.Strip(query, out var tokens);
        _queryTokens = tokens;
        var cleanQuery = SearchQuerySortParser.StripExclusionBypass(strippedTrailing, out var bypassExclusions);

        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            ClearResults();
            return;
        }

        _searchEngine.QueueSearch(
            cleanQuery,
            searchScope: null,
            isInlineSearchContext: false,
            fileLimit: SearchViewModel.FullSearchFileLimit,
            appLimit: SearchViewModel.FullSearchAppLimit,
            resultMapper: (fileResults, _) =>
            {
                var results = new List<AppSearchResult>();
                SearchResultMapper.RemoveQueriedDirectoryItself(fileResults, cleanQuery);
                if (fileResults != null)
                {
                    // Local (USN-indexed) and network-drive results stream in from separate,
                    // independently-timed sources (see Core.Services.SearchService.SearchStreamingAsync's
                    // localTask/networkTask) and land in fileResults in WHATEVER order they happened to
                    // arrive -- not relevance order. SearchResultMapper.BuildQuickResults (the quick/inline
                    // windows) already re-sorts by rank before building rows; this window skipped that
                    // step, so a fast-arriving low-relevance network match could sit ahead of a slower
                    // but far more relevant local one.
                    fileResults.Sort(new SearchResultRankComparer(SearchHistoryStore.Snapshot()));
                    for (var i = 0; i < fileResults.Count; i++)
                    {
                        results.Add(SearchResultMapper.CreateUiResult(fileResults[i], cleanQuery, results.Count, isApplication: false, scope: null));
                    }
                }
                return results;
            },
            searching => _setIsSearching(searching),
            (results, status, final) =>
            {
                _serviceStatus.ClearReconnectState();
                _setLoadingPanelVisibility(Visibility.Collapsed);
                _setIsSearchBoxEnabled(true);
                // This window has its own "no results" hint (ShowNoResultsHint, keyed off an empty
                // FilteredResults) -- the shared engine's synthetic "Empty" placeholder row is meant
                // for the quick/inline windows, which have no such hint and render it inline instead.
                // Left in here, it counts toward FilteredResults.Count and shows up as a real grid row.
                var filteredResults = results.Where(r => !r.IsEmptyResult).ToList();
                _setAllResults(filteredResults);
                // Token providers (e.g. the built-in ":[SCMA]"/".ext"/"::expr" sort+filter+match
                // plugin) render via a follow-up ApplyFiltersAndRender inside
                // RefreshAfterTokenDispatchAsync instead of the call below -- a provider with no
                // genuine async work (a plain filter, no metadata fetch) resolves its
                // already-completed Task inline, so RefreshAfterTokenDispatchAsync can run to
                // completion synchronously right here; rendering the raw (pre-token) results below
                // would then immediately clobber its filtered result with the unfiltered one.
                if (_queryTokens.Count > 0)
                    _ = RefreshAfterTokenDispatchAsync(filteredResults, _queryTokens);
                else
                    _applyFiltersAndRender();
                if (final)
                    _setIsSearching(false);
            },
            () => _serviceStatus.CheckServiceStatusOnStartup(),
            // Unlike the quick/inline windows' SearchResultMapper.BuildQuickResults, this window's own
            // resultMapper above only ever builds rows from real file matches -- it never folds instant
            // results (a pasted URL, a calculator expression, ...) into the final render at all. Left at
            // the default (emit unconditionally), SearchExecutionEngine.PerformSearch would still show
            // that instant row the moment it's typed, only for the follow-up file-search render (which
            // finds no file matches for something like a URL) to immediately wipe it back out -- a
            // flash-then-vanish row that doesn't belong in this window's file-browser-style grid anyway
            // (an "InstantResult" row has no real path/size/type, so those columns render nonsense for
            // it). Suppressing the up-front emission here means instant results simply never appear in
            // this window, matching that the settled render never included them to begin with.
            shouldEmitInstantResults: () => false,
            bypassExclusions: bypassExclusions
        );
    }

    private async Task RefreshAfterTokenDispatchAsync(List<AppSearchResult> resultsSnapshot, IReadOnlyList<string> tokensSnapshot)
    {
        var dispatched = await QueryTokenDispatcher.ApplyAsync(resultsSnapshot, tokensSnapshot);
        if (!ReferenceEquals(_getAllResults(), resultsSnapshot) || !ReferenceEquals(_queryTokens, tokensSnapshot))
            return;
        _setAllResults(dispatched);
        _applyFiltersAndRender();
    }

    public void PerformSearch(string query)
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
        _setIsSearching(false);
        _getAllResults().Clear();
        _applyFiltersAndRender();
        _setLoadingPanelVisibility(Visibility.Collapsed);
    }
}
