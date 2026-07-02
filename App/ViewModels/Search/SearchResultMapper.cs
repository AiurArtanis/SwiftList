using System.IO;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Search;

public static class SearchResultMapper
{
    public static List<AppSearchResult> BuildQuickResults(List<SearchResult>? fileResults, string query, string? scope, string? contextDirectory, bool isInlineWindow)
    {
        var uiResults = new List<AppSearchResult>();
        PluginSearchResultMapper.AddInstantResults(uiResults, query, isInlineWindow);

        if (fileResults != null && !string.IsNullOrWhiteSpace(query))
        {
            try
            {
                var trimmed = query.Trim();
                var endsWithSeparator = trimmed.EndsWith("\\") || trimmed.EndsWith("/");
                if (trimmed.EndsWith(":\\") || trimmed.EndsWith(":/") || (endsWithSeparator && Directory.Exists(trimmed)))
                {
                    var normalizedQuery = SearchResultHelper.NormalizePath(trimmed);
                    fileResults.RemoveAll(x => string.Equals(SearchResultHelper.NormalizePath(x.Path), normalizedQuery, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch { }
        }

        // If a directory scope is provided, keep only file/folder results that reside inside the scoped path.
        if (!string.IsNullOrEmpty(scope) && fileResults != null)
        {
            var normalizedScope = SearchResultHelper.NormalizePath(scope);
            fileResults = fileResults.FindAll(x =>
            {
                var normalizedPath = SearchResultHelper.NormalizePath(x.Path);
                return SearchResultHelper.IsPathInsideScope(normalizedPath, normalizedScope)
                    && !string.Equals(normalizedPath, normalizedScope, StringComparison.OrdinalIgnoreCase);
            });
        }

        var historySnapshot = SearchHistoryStore.Snapshot();
        var comparer = new SearchResultRankComparer(historySnapshot);
        fileResults?.Sort(comparer);

        // Add history/favorites first (Highest priority result group)
        AddHistoryPriorityResults(uiResults, fileResults, query, scope, historySnapshot);

        SearchableItemMapper.AddSearchableItemResults(uiResults, query, isInlineWindow);
        var hasPluginSearchActions = PluginSearchResultMapper.AddPluginSearchActionResults(uiResults, query, contextDirectory, isInlineWindow);

        if (fileResults != null)
        {
            var existingPaths = new HashSet<string>(uiResults.Select(r => SearchResultHelper.NormalizePath(r.FullPath)), StringComparer.OrdinalIgnoreCase);
            fileResults.RemoveAll(r => existingPaths.Contains(SearchResultHelper.NormalizePath(r.Path)));
        }

        var fileResultsCount = fileResults != null ? fileResults.Count : 0;
        var hasInstantResults = uiResults.Any(x => x.IsInstantResult);
        if (uiResults.Count + fileResultsCount < 10 && fileResults != null)
        {
            for (var i = 0; i < fileResultsCount; i++)
            {
                uiResults.Add(SearchResultHelper.CreateUiResult(fileResults[i], query, uiResults.Count, isApplication: false, scope));
            }

            return uiResults;
        }

        var firstFileCount = Math.Min(fileResultsCount, Math.Max(0, 8 - uiResults.Count));
        if (fileResults != null)
        {
            for (var i = 0; i < firstFileCount; i++)
            {
                uiResults.Add(SearchResultHelper.CreateUiResult(fileResults[i], query, uiResults.Count, isApplication: false, scope));
            }
        }

        if (!hasInstantResults)
        {
            SearchResultHelper.AddShowMoreResult(uiResults, query);
        }

        var hasMoreAtEnd = fileResultsCount > 50;
        var endLimit = hasMoreAtEnd ? 50 : fileResultsCount;

        if (fileResults != null)
        {
            for (var i = 0; i < endLimit; i++)
            {
                if (i >= firstFileCount)
                {
                    uiResults.Add(SearchResultHelper.CreateUiResult(fileResults[i], query, uiResults.Count, isApplication: false, scope));
                }
            }
        }

        if (hasMoreAtEnd && !hasInstantResults)
        {
            SearchResultHelper.AddShowMoreResult(uiResults, query);
        }

        return uiResults;
    }

    private class PriorityCandidate
    {
        public SearchResult? Result { get; set; }
        public FavoriteItemSetting? Favorite { get; set; }
        public bool IsApplication { get; set; }
        public int Priority { get; set; }
        public string NormalizedPath { get; set; } = string.Empty;
    }

    public static void AddHistoryPriorityResults(
        List<AppSearchResult> uiResults,
        List<SearchResult>? fileResults,
        string query,
        string? scope,
        IReadOnlyDictionary<string, int> historySnapshot)
    {
        var availableSlots = Math.Max(0, 9 - uiResults.Count);
        if (availableSlots == 0)
            return;

        var candidates = new List<PriorityCandidate>();

        var favorites = UserSettings.Load().Favorites;
        if (!string.IsNullOrWhiteSpace(query))
        {
            for (var i = 0; i < favorites.Count; i++)
            {
                var fav = favorites[i];
                if (FavoriteSearchHelper.IsFavoriteMatch(fav, query))
                {
                    var lookupPath = fav.Path.Length > 3 && fav.Path[^1] == '\\' ? fav.Path.TrimEnd('\\') : fav.Path;
                    var priority = historySnapshot.TryGetValue(lookupPath, out var hp) ? hp : 0;

                    candidates.Add(new PriorityCandidate
                    {
                        Favorite = fav,
                        Priority = priority,
                        NormalizedPath = SearchResultHelper.NormalizePath(fav.Path)
                    });
                }
            }
        }

        if (fileResults != null)
        {
            foreach (var result in fileResults)
            {
                var lookupPath = result.Path.Length > 3 && result.Path[^1] == '\\' ? result.Path.TrimEnd('\\') : result.Path;
                if (historySnapshot.TryGetValue(lookupPath, out var priority))
                {
                    candidates.Add(new PriorityCandidate
                    {
                        Result = result,
                        IsApplication = false,
                        Priority = priority,
                        NormalizedPath = SearchResultHelper.NormalizePath(result.Path)
                    });
                }
            }
        }

        if (candidates.Count == 0)
            return;

        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates
                     .OrderBy(x => x.Priority)
                     .ThenBy(x => x.NormalizedPath.Length)
                     .ThenBy(x => x.NormalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            if (uiResults.Count >= 9)
                break;

            if (!usedPaths.Add(candidate.NormalizedPath))
                continue;

            if (candidate.Favorite != null)
            {
                uiResults.Add(FavoriteSearchHelper.CreateFavoriteUiResult(candidate.Favorite, query, uiResults.Count));
            }
            else if (candidate.Result != null)
            {
                uiResults.Add(SearchResultHelper.CreateUiResult(candidate.Result, query, uiResults.Count, candidate.IsApplication, scope));
            }

            var matchedFile = fileResults?.FirstOrDefault(r => SearchResultHelper.NormalizePath(r.Path).Equals(candidate.NormalizedPath, StringComparison.OrdinalIgnoreCase));
            if (matchedFile != null && fileResults != null)
            {
                fileResults.Remove(matchedFile);
            }
        }
    }

    public static AppSearchResult CreateUiResult(SearchResult item, string query, int index, bool isApplication, string? scope)
        => SearchResultHelper.CreateUiResult(item, query, index, isApplication, scope);

    public static AppSearchResult CreateNoResultsResult(string query)
        => SearchResultHelper.CreateNoResultsResult(query);

    public static string FormatSearchStatus(int appCount, int fileCount)
        => SearchResultHelper.FormatSearchStatus(appCount, fileCount);

    public static void AddSectionHeader(List<AppSearchResult> uiResults, string title, string query)
        => SearchResultHelper.AddSectionHeader(uiResults, title, query);
}
