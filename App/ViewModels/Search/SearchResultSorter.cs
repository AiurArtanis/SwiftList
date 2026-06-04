using System;
using System.Collections.Generic;
using System.Linq;
using SwiftList.Core;
using SwiftList.App.Services;
using SwiftList.PluginSdk;

namespace SwiftList.App.ViewModels.Search
{
    internal static class SearchResultSorter
    {
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

            string columnId = currentSortColumn;
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
        public CustomSearchResultComparer(Func<ISearchResult, ISearchResult, int> comparer)
        {
            _comparer = comparer;
        }
        public int Compare(AppSearchResult? x, AppSearchResult? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            return _comparer(x, y);
        }
    }
}
