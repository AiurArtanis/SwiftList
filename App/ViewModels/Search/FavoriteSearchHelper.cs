using System.IO;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;
using SwiftList.PluginSdk.Helpers;

using SwiftList.Core.SearchIndex;
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

    // The standard match+weight contract (FuzzyMatcher.ComputeBestMatch): display name first, then
    // the raw path, via the same FzfPattern.Parse Core's real file search uses -- a multi-word query
    // requires all its words to match somewhere, the same way it would against an indexed file name,
    // instead of the old displayName.Contains/.Contains(Path)/MarkFuzzyMatch chain treating the whole
    // query (spaces included) as one literal/fuzzy string.
    internal static (bool IsMatch, double Weight) ComputeMatch(FavoriteItemSetting fav, string query)
        => FuzzyMatcher.ComputeBestMatch(query, GetDisplayName(fav), new[] { fav.Path });

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
