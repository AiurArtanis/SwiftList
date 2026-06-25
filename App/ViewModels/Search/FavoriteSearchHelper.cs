using System.IO;
using SwiftList.App.Services;
using SwiftList.Core;
using SwiftList.PluginSdk;

namespace SwiftList.App.ViewModels.Search;

public static class FavoriteSearchHelper
{
    public static bool IsFavoriteMatch(FavoriteItemSetting fav, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;

        var displayName = fav.Name;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            if (fav.Path.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase) || fav.Path.StartsWith("::", StringComparison.OrdinalIgnoreCase))
            {
                displayName = ShellPathHelper.GetVirtualFolderDisplayName(fav.Path, fav.Path);
            }
            else
            {
                try
                {
                    displayName = Path.GetFileName(fav.Path.TrimEnd('\\', '/'));
                }
                catch { }
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = fav.Path;
                }
            }
        }

        if (displayName.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (fav.Path.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;

        var highlights = new bool[displayName.Length];
        Converters.FuzzyHighlightMatcher.MarkFuzzyMatch(displayName.ToLowerInvariant(), query.ToLowerInvariant(), highlights);
        if (highlights.Any(h => h)) return true;

        return false;
    }

    public static AppSearchResult CreateFavoriteUiResult(FavoriteItemSetting fav, string query, int index)
    {
        var isDir = fav.Path.StartsWith("::") || fav.Path.StartsWith("shell:") || Directory.Exists(fav.Path);
        var label = TranslationManager.Instance["Search_ResultFavorite"] ?? "Favorite";

        var displayName = fav.Name;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            if (fav.Path.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase) || fav.Path.StartsWith("::", StringComparison.OrdinalIgnoreCase))
            {
                displayName = ShellPathHelper.GetVirtualFolderDisplayName(fav.Path, fav.Path);
            }
            else
            {
                try
                {
                    displayName = Path.GetFileName(fav.Path.TrimEnd('\\', '/'));
                }
                catch { }
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = fav.Path;
                }
            }
        }

        return new AppSearchResult
        {
            Name = displayName,
            FullPath = fav.Path,
            ParentDir = "★ " + label,
            ContextDirectory = isDir ? fav.Path : (Path.GetDirectoryName(fav.Path) ?? fav.Path),
            IsDir = isDir,
            Drive = string.Empty,
            ResultKind = "File",
            Index = index,
            SearchQuery = query
        };
    }
}
