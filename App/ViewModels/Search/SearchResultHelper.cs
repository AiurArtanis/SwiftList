using System.IO;
using SwiftList.Core;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Search;

internal static class SearchResultHelper
{
    public static void AddSectionHeader(List<AppSearchResult> uiResults, string title, string query) => uiResults.Add(new AppSearchResult
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

    public static AppSearchResult CreateUiResult(SearchResult item, string query, int index, bool isApplication, string? scope)
    {
        var parentDir = Path.GetDirectoryName(item.Path);
        return new AppSearchResult
        {
            Name = string.IsNullOrWhiteSpace(item.Name) ? item.Path : item.Name,
            FullPath = item.Path,
            ParentDir = GetParentDisplayText(item, isApplication, scope),
            ContextDirectory = item.IsDir ? item.Path : (parentDir ?? item.Drive + ":\\"),
            IsDir = item.IsDir,
            Drive = item.Drive.ToString(),
            ResultKind = isApplication ? "Application" : "File",
            Index = index,
            SearchQuery = query
        };
    }

    public static AppSearchResult CreateNoResultsResult(string query) => new AppSearchResult
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

    public static string GetParentDisplayText(SearchResult item, bool isApplication, string? scope)
    {
        var parentDir = Path.GetDirectoryName(item.Path);
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

        var path = parentDir ?? string.Empty;
        if (path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = path.Substring(@"\\wsl.localhost\".Length).Replace('\\', '/');
            var firstSlash = suffix.IndexOf('/');
            return firstSlash < 0 ? $"WSL-{suffix}:/" : $"WSL-{suffix.Substring(0, firstSlash)}:{suffix.Substring(firstSlash)}";
        }
        return path;
    }

    public static string FormatRelativeParentPath(string parentDir, string scope)
    {
        var relativePath = Path.GetRelativePath(scope, parentDir);
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
        {
            return string.Empty;
        }

        return relativePath.StartsWith(".\\", StringComparison.Ordinal)
            ? relativePath[2..]
            : relativePath;
    }

    public static string NormalizePath(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static bool IsPathInsideScope(string normalizedPath, string normalizedScope) => normalizedPath.StartsWith(normalizedScope + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedScope + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void AddShowMoreResult(List<AppSearchResult> uiResults, string query) => uiResults.Add(new AppSearchResult
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
