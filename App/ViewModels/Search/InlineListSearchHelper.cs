using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SwiftList.Core;
using SwiftList.PluginSdk;
using SwiftList.App.Services;
using SwiftList.App.Converters;

namespace SwiftList.App.ViewModels.Search
{
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

            bool hasPluginSearchActions = PluginSearchResultMapper.AddPluginSearchActionResults(uiResults, query, contextDirectory, isInlineWindow: true);

            var listResults = new List<AppSearchResult>();
            try
            {
                int index = 0;
                foreach (var item in rawItems)
                {
                    if (string.IsNullOrWhiteSpace(item))
                        continue;

                    bool isFullPath = Path.IsPathRooted(item);
                    string displayName = isFullPath
                        ? Path.GetFileName(item.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                        : item;
                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = item;

                    bool isMatch = displayName.Contains(query, StringComparison.OrdinalIgnoreCase);
                    if (!isMatch)
                    {
                        var highlights = new bool[displayName.Length];
                        FuzzyHighlightMatcher.MarkFuzzyMatch(displayName, query, highlights);
                        isMatch = highlights.Any(h => h);
                    }

                    if (isMatch)
                    {
                        AppSearchResult result;
                        if (isFullPath)
                        {
                            bool isDir = Directory.Exists(item);
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
                    }
                }
            }
            catch (Exception ex)
            {
                SwiftList.Core.Logger.Log($"[InlineListSearchHelper] ListProvider search error: {ex.Message}", SwiftList.Core.LogLevel.Error);
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

                string statusText = uiResults.Count == 1 && uiResults[0].IsEmptyResult
                    ? "No matching results"
                    : string.Format(TranslationManager.Instance["Search_StatsFilesOnly"], uiResults.Count);

                onResultsUpdated(uiResults, statusText, true);
            }));
        }
    }
}
