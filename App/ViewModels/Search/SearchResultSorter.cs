using SwiftList.Core;
using SwiftList.App.Services;
using SwiftList.PluginSdk.Abstractions;

namespace SwiftList.App.ViewModels.Search;

internal static class SearchResultSorter
{
    // Applies a query-typed ":[SCMA]" sort suffix -- takes priority over the column-header sort
    // state, in the given priority order (first directive is the primary key). Only reorders
    // ordinary file/app/folder results; section headers, instant results, plugin actions, and other
    // synthetic rows (relevant in the quick/inline windows' mixed result lists) stay at their
    // original position.
    public static IEnumerable<AppSearchResult> SortByQueryDirectives(IEnumerable<AppSearchResult> resultsList, IReadOnlyList<QuerySortDirective> directives)
    {
        if (directives.Count == 0)
            return resultsList;

        var list = resultsList as IList<AppSearchResult> ?? resultsList.ToList();

        var ordinaryIndices = new List<int>();
        var ordinaryItems = new List<AppSearchResult>();
        for (var i = 0; i < list.Count; i++)
        {
            if (IsOrdinaryResult(list[i]))
            {
                ordinaryIndices.Add(i);
                ordinaryItems.Add(list[i]);
            }
        }
        if (ordinaryItems.Count == 0)
            return list;

        IOrderedEnumerable<AppSearchResult>? ordered = null;
        foreach (var directive in directives)
        {
            Func<AppSearchResult, IComparable> keySelector = directive.Field switch
            {
                QuerySortField.Size => r => r.Size,
                QuerySortField.Created => r => r.DateCreated,
                QuerySortField.Modified => r => r.DateModified,
                QuerySortField.Accessed => r => r.DateAccessed,
                _ => r => r.Name
            };

            ordered = ordered == null
                ? (directive.Descending ? ordinaryItems.OrderByDescending(keySelector) : ordinaryItems.OrderBy(keySelector))
                : (directive.Descending ? ordered.ThenByDescending(keySelector) : ordered.ThenBy(keySelector));
        }
        var sortedOrdinary = ordered!.ToList();

        var output = new List<AppSearchResult>(list);
        for (var i = 0; i < ordinaryIndices.Count; i++)
            output[ordinaryIndices[i]] = sortedOrdinary[i];

        return output;
    }

    // Folders are ResultKind "File" too (IsDir just flags them) -- "Application" is the only other
    // kind that's a genuine file-backed result with a real Size/timestamps worth sorting by.
    private static bool IsOrdinaryResult(AppSearchResult r) => r.ResultKind is "File" or "Application";

    // Size/DateCreated/DateModified/DateAccessed are lazily stat'd in the background -- sorting by
    // them the instant results first render just compares placeholder defaults. Callers doing a
    // query-driven sort should await this once, then re-sort/re-render so the order reflects real
    // values instead of silently looking like the sort never applied.
    public static Task PreloadMetadataForOrdinaryResultsAsync(IEnumerable<AppSearchResult> resultsList) =>
        Task.WhenAll(resultsList.Where(IsOrdinaryResult).Select(r => r.EnsureFileMetadataLoadedAsync()));

    // Shared by both search windows: preload the metadata a query-driven sort depends on, then hand
    // back to the caller to re-sort/re-render -- but only if `isStillCurrent` still says so, since a
    // newer search may have superseded this one while the preload was in flight.
    public static async Task RefreshAfterMetadataLoadedAsync(IEnumerable<AppSearchResult> resultsSnapshot, Func<bool> isStillCurrent, Action onRefresh)
    {
        await PreloadMetadataForOrdinaryResultsAsync(resultsSnapshot);
        if (isStillCurrent())
            onRefresh();
    }

    public static IEnumerable<AppSearchResult> Sort(IEnumerable<AppSearchResult> resultsList, string currentSortColumn, bool isSortAscending)
    {
        if (string.IsNullOrEmpty(currentSortColumn))
            return resultsList;

        if (currentSortColumn == TranslationManager.Instance["Search_HeaderName"])
        {
            return isSortAscending
                ? resultsList.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                : resultsList.OrderByDescending(r => r.Name, StringComparer.CurrentCultureIgnoreCase);
        }
        if (currentSortColumn == TranslationManager.Instance["Search_HeaderPath"])
        {
            return isSortAscending
                ? resultsList.OrderBy(r => r.FullPath, StringComparer.CurrentCultureIgnoreCase)
                : resultsList.OrderByDescending(r => r.FullPath, StringComparer.CurrentCultureIgnoreCase);
        }
        if (currentSortColumn == TranslationManager.Instance["Search_HeaderDateModified"])
        {
            return isSortAscending
                ? resultsList.OrderBy(r => r.DateModified)
                : resultsList.OrderByDescending(r => r.DateModified);
        }

        Func<ISearchResult, ISearchResult, int>? customComparer = null;
        foreach (var provider in PluginManager.Instance.ResultColumnProviders)
        {
            var col = provider.GetColumns().FirstOrDefault(c => c.HeaderText.Equals(currentSortColumn, StringComparison.OrdinalIgnoreCase) || c.ColumnId.Equals(currentSortColumn, StringComparison.OrdinalIgnoreCase));
            if (col != null && col.SortComparer != null)
            {
                customComparer = col.SortComparer;
                break;
            }
        }

        if (customComparer != null)
        {
            return isSortAscending
                ? resultsList.OrderBy(r => r, new CustomSearchResultComparer(customComparer))
                : resultsList.OrderByDescending(r => r, new CustomSearchResultComparer(customComparer));
        }

        var columnId = currentSortColumn;
        foreach (var provider in PluginManager.Instance.ResultColumnProviders)
        {
            var col = provider.GetColumns().FirstOrDefault(c => c.HeaderText.Equals(currentSortColumn, StringComparison.OrdinalIgnoreCase));
            if (col != null)
            {
                columnId = col.ColumnId;
                break;
            }
        }
        return isSortAscending
            ? resultsList.OrderBy(r => r[columnId], StringComparer.CurrentCultureIgnoreCase)
            : resultsList.OrderByDescending(r => r[columnId], StringComparer.CurrentCultureIgnoreCase);
    }
}

internal class CustomSearchResultComparer : IComparer<AppSearchResult>
{
    private readonly Func<ISearchResult, ISearchResult, int> _comparer;
    public CustomSearchResultComparer(Func<ISearchResult, ISearchResult, int> comparer) => _comparer = comparer;
    public int Compare(AppSearchResult? x, AppSearchResult? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        return _comparer(x, y);
    }
}
