using SwiftList.PluginSdk.Abstractions;

namespace SwiftList.App.ViewModels.Search;

// Applies the active sidebar filter predicates (batch async, may fetch metadata over IPC) on top
// of an already-sorted result list. Renders immediately with the sorted-but-unfiltered list, then
// swaps in the filtered list once the predicates resolve -- discarding a stale resolution if a
// newer results set or filter selection has since taken over.
internal sealed class DynamicFilterCoordinator
{
    private List<Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>>>? _pendingFilters;

    public void Apply(
        List<AppSearchResult> allResults,
        List<Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>>> activeFilters,
        Func<IEnumerable<AppSearchResult>, List<AppSearchResult>> sort,
        Func<List<AppSearchResult>> currentResults,
        Action<List<AppSearchResult>> render)
    {
        var sorted = sort(allResults);

        if (activeFilters.Count == 0)
        {
            _pendingFilters = null;
            render(sorted);
            return;
        }

        render(sorted);
        _pendingFilters = activeFilters;
        _ = ApplyAsync(allResults, activeFilters, sort, currentResults, render);
    }

    private async Task ApplyAsync(
        List<AppSearchResult> resultsSnapshot,
        List<Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>>> filtersSnapshot,
        Func<IEnumerable<AppSearchResult>, List<AppSearchResult>> sort,
        Func<List<AppSearchResult>> currentResults,
        Action<List<AppSearchResult>> render)
    {
        IReadOnlyList<ISearchResult> current = resultsSnapshot;
        foreach (var filter in filtersSnapshot)
            current = await filter(current);

        if (!ReferenceEquals(currentResults(), resultsSnapshot) || !ReferenceEquals(_pendingFilters, filtersSnapshot))
            return;

        render(sort(current.Cast<AppSearchResult>()));
    }
}
