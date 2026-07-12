using System.IO;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.App.Services;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Search;

internal static class InlineListSearchHelper
{
    public static void PerformInlineListProviderSearch(
        string query,
        IInlineSearchAdapter adapter,
        IntPtr targetHwnd,
        IEnumerable<string> rawItems,
        string? contextDirectory,
        int searchVersion,
        Func<int> getLatestSearchVersion,
        Action<List<AppSearchResult>, string, bool> onResultsUpdated,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var uiResults = new List<AppSearchResult>();

        var hasPluginSearchActions = PluginSearchResultMapper.AddPluginSearchActionResults(uiResults, query, contextDirectory, isInlineWindow: true);

        // Weight carried alongside each result (rather than sorting AppSearchResult itself) so the
        // major-category grouping this method and MergeLocalMatches build around (instant results,
        // then this section) stays exactly where it was -- only the ORDER WITHIN this section changes,
        // from raw rawItems encounter order to match-quality order.
        var listResults = new List<(AppSearchResult Result, double Weight)>();
        try
        {
            var index = 0;
            var lastUpdateCount = 0;
            var lastUpdateTime = DateTime.UtcNow;

            foreach (var item in rawItems)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(item))
                    continue;

                if (!TryBuildMatch(item, query, index, out var result, out var weight))
                    continue;

                listResults.Add((result, weight));
                index++;

                if (listResults.Count - lastUpdateCount >= 50 || (DateTime.UtcNow - lastUpdateTime).TotalMilliseconds > 100)
                {
                    var sortedSoFar = listResults.OrderByDescending(r => r.Weight).ToList();
                    var partialResults = new List<AppSearchResult>(uiResults);
                    if (hasPluginSearchActions && sortedSoFar.Count > 0)
                    {
                        SearchResultMapper.AddSectionHeader(partialResults, TranslationManager.Instance["Search_SectionHeader"], query);
                    }
                    foreach (var (res, _) in sortedSoFar)
                    {
                        res.Index = partialResults.Count;
                        partialResults.Add(res);
                    }

                    System.Windows.Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        if (searchVersion == getLatestSearchVersion() && !token.IsCancellationRequested)
                        {
                            var status = string.Format(TranslationManager.Instance["Search_StatsFilesOnly"], partialResults.Count);
                            onResultsUpdated(partialResults, status, false);
                        }
                    }));

                    lastUpdateCount = listResults.Count;
                    lastUpdateTime = DateTime.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[InlineListSearchHelper] ListProvider search error: {ex.Message}", Core.LogLevel.Error);
        }

        if (hasPluginSearchActions && listResults.Count > 0)
        {
            SearchResultMapper.AddSectionHeader(uiResults, TranslationManager.Instance["Search_SectionHeader"], query);
        }

        foreach (var (res, _) in listResults.OrderByDescending(r => r.Weight))
        {
            res.Index = uiResults.Count;
            uiResults.Add(res);
        }

        if (uiResults.Count == 0)
        {
            uiResults.Add(SearchResultMapper.CreateNoResultsResult(query));
        }

        System.Windows.Application.Current.Dispatcher.Invoke(new Action(() =>
        {
            if (searchVersion != getLatestSearchVersion() || token.IsCancellationRequested)
                return;

            var statusText = uiResults.Count == 1 && uiResults[0].IsEmptyResult
                ? "No matching results"
                : string.Format(TranslationManager.Instance["Search_StatsFilesOnly"], uiResults.Count);

            onResultsUpdated(uiResults, statusText, true);
        }));
    }

    public static List<AppSearchResult> GetLocalMatches(
        string query,
        IEnumerable<string> rawItems,
        string? contextDirectory,
        CancellationToken token)
    {
        var results = new List<(AppSearchResult Result, double Weight)>();
        token.ThrowIfCancellationRequested();

        var index = 0;
        foreach (var item in rawItems)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item))
                continue;

            if (!TryBuildMatch(item, query, index, out var result, out var weight))
                continue;

            results.Add((result, weight));
            index++;
        }
        return results.OrderByDescending(r => r.Weight).Select(r => r.Result).ToList();
    }

    // Shared by PerformInlineListProviderSearch and GetLocalMatches: both walk the same raw item
    // list, judging fuzzy-match against the display name and building the same AppSearchResult shape.
    // Ranking-only: callers sort their accumulated matches by Weight (descending) before finalizing,
    // instead of leaving them in raw source-list encounter order.
    private static bool TryBuildMatch(string item, string query, int index, out AppSearchResult result, out double weight)
    {
        weight = 0;
        var isFullPath = false;
        try
        {
            isFullPath = Path.IsPathRooted(item);
        }
        catch { }

        var displayName = item;
        if (isFullPath)
        {
            try
            {
                displayName = Path.GetFileName(item.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            catch { }
        }
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = item;

        // The standard match+weight contract (FuzzyMatcher.ComputeBestMatch) -- same FzfPattern.Parse
        // Core's real file search uses, so a multi-word query requires all its words to match
        // somewhere instead of the old displayName.Contains/MarkFuzzyMatch chain treating the whole
        // query (spaces included) as one literal/fuzzy string.
        var (isMatch, weightValue) = FuzzyMatcher.ComputeBestMatch(query, displayName);
        weight = weightValue;

        if (!isMatch)
        {
            result = null!;
            return false;
        }

        if (isFullPath)
        {
            var isDir = false;
            try { isDir = Directory.Exists(item); } catch { }
            result = new AppSearchResult
            {
                Name = displayName,
                FullPath = item,
                ParentDir = string.Empty,
                ContextDirectory = isDir ? item : (Path.GetDirectoryName(item) ?? string.Empty),
                IsDir = isDir,
                Drive = Path.GetPathRoot(item)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar) ?? string.Empty,
                ResultKind = "File",
                Index = index,
                SearchQuery = query
            };
        }
        else
        {
            result = new AppSearchResult
            {
                Name = displayName,
                FullPath = item,
                ResultKind = "ListItem",
                Index = index,
                SearchQuery = query
            };
        }
        return true;
    }

    public static List<AppSearchResult> MergeLocalMatches(
        List<AppSearchResult> uiResults,
        List<AppSearchResult> localMatches,
        string query)
    {
        var combinedResults = new List<AppSearchResult>();
        var instantItems = new List<AppSearchResult>();
        var globalItems = new List<AppSearchResult>();
        var passedHeader = false;
        var searchHeaderTitle = TranslationManager.Instance["Search_SectionHeader"];

        foreach (var item in uiResults)
        {
            if (!passedHeader)
            {
                if (item.ResultKind == "SectionHeader" && item.Name == searchHeaderTitle)
                {
                    passedHeader = true;
                    continue;
                }
                if (item.IsInstantResult || item.IsPluginSearchAction || item.ResultKind == "SectionHeader")
                {
                    instantItems.Add(item);
                }
                else
                {
                    passedHeader = true;
                    globalItems.Add(item);
                }
            }
            else
            {
                globalItems.Add(item);
            }
        }

        combinedResults.AddRange(instantItems);
        // Guarded the same way the "Global Search" header below is -- an empty "Current Folder"
        // section with nothing under it is misleading on its own, and (since a SectionHeader isn't an
        // "ordinary" File/Application row) it would also survive a query-token filter that finds
        // nothing, leaving a header with no results and no "no results" placeholder either.
        if (localMatches.Count > 0)
        {
            SearchResultMapper.AddSectionHeader(combinedResults, TranslationManager.Instance["Search_LocalFolderHeader"], query);
            combinedResults.AddRange(localMatches);
        }

        if (globalItems.Count > 0)
        {
            SearchResultMapper.AddSectionHeader(combinedResults, TranslationManager.Instance["Search_GlobalSearchHeader"], query);
            combinedResults.AddRange(globalItems);
        }

        for (var idx = 0; idx < combinedResults.Count; idx++)
        {
            combinedResults[idx].Index = idx;
        }
        return combinedResults;
    }
}
