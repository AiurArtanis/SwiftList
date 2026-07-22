using SwiftList.App.Helpers;
using SwiftList.App.Services.PluginManagerCore;
using SwiftList.App.ViewModels.Settings.Plugins;
using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.App.ViewModels.Search;

// Resolves the "type" tier a BuildQuickResults candidate belongs to for the quick window's
// user-orderable result-type-priority feature (UserSettings.ResultTypeOrder) -- generalizes what used
// to be a single "boost applications" toggle into an id per ISearchableItemProvider (Applications,
// Settings, File Filters, any third-party plugin) plus one synthetic id for raw file-index results.
public static class SearchResultTypePriority
{
    // No ISearchableItemProvider sits behind the fileResults candidate loop in BuildQuickResults --
    // mirrors the "__builtin::..." synthetic-id convention StartupPanelSettings.TabOrder already uses
    // for its own non-plugin entries.
    public const string FilesTypeId = "__builtin::Files";

    public static string GetProviderTypeId(ISearchableItemProvider provider) =>
        PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(provider), PluginComponentType.SearchableItemProvider, provider.GetType().Name);

    // Position in the user's saved order (most-preferred first); an id that isn't listed yet falls back
    // to int.MaxValue, which -- since the caller's sort is stable -- lands it after every listed type
    // while preserving its original relative order against any OTHER unlisted type, matching the same
    // fallback convention QuickNavigationProviderOrder/StartupPanel.TabOrder already use.
    public static int Rank(string typeId, List<string> order)
    {
        var idx = order.IndexOf(typeId);
        return idx >= 0 ? idx : int.MaxValue;
    }
}
