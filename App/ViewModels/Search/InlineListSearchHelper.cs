using System.IO;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.App.Services;
using SwiftList.App.Converters;

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

        var listResults = new List<AppSearchResult>();
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

                var isMatch = displayName.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!isMatch)
                {
                    var highlights = new bool[displayName.Length];
                    FuzzyHighlightMatcher.MarkFuzzyMatch(displayName, query, highlights, token);
                    isMatch = highlights.Any(h => h);
                }

                if (isMatch)
                {
                    AppSearchResult result;
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
                    listResults.Add(result);
                    index++;

                    if (listResults.Count - lastUpdateCount >= 50 || (DateTime.UtcNow - lastUpdateTime).TotalMilliseconds > 100)
                    {
                        var partialResults = new List<AppSearchResult>(uiResults);
                        if (hasPluginSearchActions && listResults.Count > 0)
                        {
                            SearchResultMapper.AddSectionHeader(partialResults, TranslationManager.Instance["Search_SectionHeader"], query);
                        }
                        foreach (var res in listResults)
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
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[InlineListSearchHelper] ListProvider search error: {ex.Message}", Core.LogLevel.Error);
        }

        if (hasPluginSearchActions && listResults.Count > 0)
        {
            SearchResultMapper.AddSectionHeader(uiResults, TranslationManager.Instance["Search_SectionHeader"], query);
        }

        foreach (var res in listResults)
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
        var results = new List<AppSearchResult>();
        token.ThrowIfCancellationRequested();

        var index = 0;
        foreach (var item in rawItems)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item))
                continue;

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

            var isMatch = displayName.Contains(query, StringComparison.OrdinalIgnoreCase);
            if (!isMatch)
            {
                var highlights = new bool[displayName.Length];
                FuzzyHighlightMatcher.MarkFuzzyMatch(displayName, query, highlights, token);
                isMatch = highlights.Any(h => h);
            }

            if (isMatch)
            {
                AppSearchResult result;
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
                results.Add(result);
                index++;
            }
        }
        return results;
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
        SearchResultMapper.AddSectionHeader(combinedResults, TranslationManager.Instance["Search_LocalFolderHeader"] ?? "Current Folder", query);
        combinedResults.AddRange(localMatches);

        if (globalItems.Count > 0)
        {
            SearchResultMapper.AddSectionHeader(combinedResults, TranslationManager.Instance["Search_GlobalSearchHeader"] ?? "Global Search", query);
            combinedResults.AddRange(globalItems);
        }

        for (var idx = 0; idx < combinedResults.Count; idx++)
        {
            combinedResults[idx].Index = idx;
        }
        return combinedResults;
    }
}
