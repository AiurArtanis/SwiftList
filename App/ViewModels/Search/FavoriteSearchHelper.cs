using System.IO;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;
using SwiftList.PluginSdk.Helpers;

namespace SwiftList.App.ViewModels.Search;

public static class FavoriteSearchHelper
{
    // Display label for a favorite: explicit Name, else virtual-folder name / full URL / file name.
    private static string GetDisplayName(FavoriteItemSetting fav)
    {
        if (!string.IsNullOrWhiteSpace(fav.Name)) return fav.Name;
        if (fav.Path.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase) || fav.Path.StartsWith("::", StringComparison.OrdinalIgnoreCase))
            return ShellPathHelper.GetVirtualFolderDisplayName(fav.Path, fav.Path);
        if (FavoriteUrlHelper.IsWebUrl(fav.Path))
            return fav.Path.Trim();
        try
        {
            var name = Path.GetFileName(fav.Path.TrimEnd('\\', '/'));
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        catch { }
        return fav.Path;
    }

    public static bool IsFavoriteMatch(FavoriteItemSetting fav, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;

        var displayName = GetDisplayName(fav);

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
        var label = TranslationManager.Instance["Search_ResultFavorite"];

        var displayName = GetDisplayName(fav);

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
            SearchQuery = query,
            // Web-address favorites have no shell icon; give them the globe icon.
            IconOverride = FavoriteUrlHelper.IsWebUrl(fav.Path) ? FavoriteUrlHelper.Icon : null
        };
    }
}
