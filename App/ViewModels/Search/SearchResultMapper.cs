using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using SwiftList.Core;
using SwiftList.App.Services;
using SwiftList.PluginSdk;

namespace SwiftList.App.ViewModels.Search
{
    public static class SearchResultMapper
    {
        public static List<AppSearchResult> BuildQuickResults(SearchResponse response, string query, string? scope, string? contextDirectory, bool isInlineWindow)
        {
            var uiResults = new List<AppSearchResult>();
            PluginSearchResultMapper.AddInstantResults(uiResults, query, isInlineWindow);
            bool hasPluginSearchActions = PluginSearchResultMapper.AddPluginSearchActionResults(uiResults, query, contextDirectory, isInlineWindow);

            var appResults = response.AppResults;
            var fileResults = response.FileResults;

            // If a directory scope is provided, filter out start menu apps completely
            // and keep only file/folder results that reside inside the scoped path.
            if (!string.IsNullOrEmpty(scope))
            {
                appResults = new List<SearchResult>();
                if (fileResults != null)
                {
                    string normalizedScope = NormalizePath(scope);
                    fileResults = fileResults.FindAll(x =>
                    {
                        string normalizedPath = NormalizePath(x.Path);
                        return IsPathInsideScope(normalizedPath, normalizedScope)
                            && !string.Equals(normalizedPath, normalizedScope, StringComparison.OrdinalIgnoreCase);
                    });
                }
            }

            appResults?.Sort(SearchResultRankComparer.Instance);
            fileResults?.Sort(SearchResultRankComparer.Instance);
            appResults ??= new List<SearchResult>();

            int appLimit = Math.Min(appResults.Count, 5);
            bool hasSearchResults = appLimit > 0 || (fileResults != null && fileResults.Count > 0);
            if (hasPluginSearchActions && hasSearchResults)
            {
                AddSectionHeader(uiResults, TranslationManager.Instance["Search_SectionHeader"], query);
            }

            AddHistoryPriorityResults(uiResults, appResults, fileResults, query, scope);

            appLimit = Math.Min(appResults.Count, Math.Max(0, 5 - uiResults.Count));
            for (int i = 0; i < appLimit; i++)
            {
                uiResults.Add(CreateUiResult(appResults[i], query, uiResults.Count, isApplication: true, scope));
            }

            bool hasMoreApps = appResults.Count > appLimit;
            int fileResultsCount = fileResults != null ? fileResults.Count : 0;
            bool hasInstantResults = uiResults.Any(x => x.IsInstantResult);
            if (!hasMoreApps && uiResults.Count + fileResultsCount < 10 && fileResults != null)
            {
                for (int i = 0; i < fileResultsCount; i++)
                {
                    uiResults.Add(CreateUiResult(fileResults[i], query, uiResults.Count, isApplication: false, scope));
                }

                return uiResults;
            }

            int firstFileCount = Math.Min(fileResultsCount, Math.Max(0, 8 - uiResults.Count));
            if (fileResults != null)
            {
                for (int i = 0; i < firstFileCount; i++)
                {
                    uiResults.Add(CreateUiResult(fileResults[i], query, uiResults.Count, isApplication: false, scope));
                }
            }

            if (!hasInstantResults)
            {
                AddShowMoreResult(uiResults, query);
            }

            bool hasMoreAtEnd = fileResultsCount > 50;
            int endLimit = hasMoreAtEnd ? 50 : fileResultsCount;

            if (fileResults != null)
            {
                for (int i = 0; i < endLimit; i++)
                {
                    if (i >= firstFileCount)
                    {
                        uiResults.Add(CreateUiResult(fileResults[i], query, uiResults.Count, isApplication: false, scope));
                    }
                }
            }

            if (hasMoreAtEnd && !hasInstantResults)
            {
                AddShowMoreResult(uiResults, query);
            }

            return uiResults;
        }

        public static void AddHistoryPriorityResults(
            List<AppSearchResult> uiResults,
            List<SearchResult> appResults,
            List<SearchResult>? fileResults,
            string query,
            string? scope)
        {
            int availableSlots = Math.Max(0, 9 - uiResults.Count);
            if (availableSlots == 0)
                return;

            var candidates = new List<(SearchResult Result, bool IsApplication, int Priority)>();
            foreach (var result in appResults)
            {
                int priority = SearchHistoryStore.GetPriority(result.Path);
                if (priority != int.MaxValue)
                    candidates.Add((result, true, priority));
            }

            if (fileResults != null)
            {
                foreach (var result in fileResults)
                {
                    int priority = SearchHistoryStore.GetPriority(result.Path);
                    if (priority != int.MaxValue)
                        candidates.Add((result, false, priority));
                }
            }

            if (candidates.Count == 0)
                return;

            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates
                         .OrderBy(x => x.Priority)
                         .ThenBy(x => x.Result.Path.Length)
                         .ThenBy(x => x.Result.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (uiResults.Count >= 9)
                    break;

                if (!usedPaths.Add(candidate.Result.Path))
                    continue;

                uiResults.Add(CreateUiResult(candidate.Result, query, uiResults.Count, candidate.IsApplication, scope));
                if (candidate.IsApplication)
                    appResults.Remove(candidate.Result);
                else
                    fileResults?.Remove(candidate.Result);
            }
        }

        public static void AddSectionHeader(List<AppSearchResult> uiResults, string title, string query)
        {
            uiResults.Add(new AppSearchResult
            {
                Name = title,
                FullPath = "__SECTION_HEADER__",
                ParentDir = string.Empty,
                IsDir = false,
                Drive = string.Empty,
                ResultKind = "SectionHeader",
                Index = uiResults.Count,
                SearchQuery = query
            });
        }

        public static AppSearchResult CreateUiResult(SearchResult item, string query, int index, bool isApplication, string? scope)
        {
            string? parentDir = Path.GetDirectoryName(item.Path);
            return new AppSearchResult
            {
                Name = item.Name,
                FullPath = item.Path,
                ParentDir = GetParentDisplayText(item, isApplication, scope),
                ContextDirectory = item.IsDir ? item.Path : (parentDir ?? item.Drive + ":\\"),
                IsDir = item.IsDir,
                Drive = item.Drive,
                ResultKind = isApplication ? "Application" : "File",
                Index = index,
                SearchQuery = query
            };
        }

        public static AppSearchResult CreateNoResultsResult(string query)
        {
            return new AppSearchResult
            {
                Name = TranslationManager.Instance["Search_NoResult"],
                FullPath = "__NO_RESULTS__",
                ParentDir = string.Empty,
                IsDir = false,
                Drive = string.Empty,
                ResultKind = "Empty",
                Index = 0,
                SearchQuery = string.Empty
            };
        }

        public static string GetParentDisplayText(SearchResult item, bool isApplication, string? scope)
        {
            string? parentDir = Path.GetDirectoryName(item.Path);
            if (isApplication)
            {
                return string.IsNullOrWhiteSpace(parentDir)
                    ? TranslationManager.Instance["Search_ResultApp"]
                    : string.Format(TranslationManager.Instance["Search_ResultAppDir"], parentDir);
            }

            if (!string.IsNullOrWhiteSpace(scope) && !string.IsNullOrWhiteSpace(parentDir))
            {
                return FormatRelativeParentPath(parentDir, scope);
            }

            return parentDir ?? item.Drive + ":\\";
        }

        public static string FormatRelativeParentPath(string parentDir, string scope)
        {
            string relativePath = Path.GetRelativePath(scope, parentDir);
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
            {
                return string.Empty;
            }

            return relativePath.StartsWith(".\\", StringComparison.Ordinal)
                ? relativePath[2..]
                : relativePath;
        }

        public static string NormalizePath(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static bool IsPathInsideScope(string normalizedPath, string normalizedScope)
        {
            return normalizedPath.StartsWith(normalizedScope + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(normalizedScope + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static void AddShowMoreResult(List<AppSearchResult> uiResults, string query)
        {
            uiResults.Add(new AppSearchResult
            {
                Name = string.Format(TranslationManager.Instance["Search_ShowMoreTitle"], query),
                FullPath = "__SHOW_MORE__",
                ParentDir = TranslationManager.Instance["Search_ShowMoreDesc"],
                IsDir = false,
                Drive = "",
                ResultKind = "Action",
                Index = uiResults.Count,
                SearchQuery = query
            });
        }

        public static string FormatSearchStatus(int appCount, int fileCount)
        {
            if (appCount > 0 && fileCount > 0)
            {
                return string.Format(TranslationManager.Instance["Search_StatsAppsAndFiles"], appCount, fileCount);
            }

            if (appCount > 0)
            {
                return string.Format(TranslationManager.Instance["Search_StatsAppsOnly"], appCount);
            }

            return string.Format(TranslationManager.Instance["Search_StatsFilesOnly"], fileCount);
        }
    }
}
