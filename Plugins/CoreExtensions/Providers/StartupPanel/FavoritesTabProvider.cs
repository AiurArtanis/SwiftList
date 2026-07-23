using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Providers.StartupPanel;

// Startup-panel tab backed by the host's own favorites (FavoritesService, already wired to
// SwiftList.Core.UserSettings.Favorites). These are user-curated and usually few, so no cap.
public class FavoritesTabProvider : IStartupPanelTabProvider
{
    public string Name => TranslationService.Get("StartupPanel_TabFavorites");

    public IEnumerable<ISearchResult> GetItems() => FavoritesService.GetFavorites()
        .Where(f => !string.IsNullOrWhiteSpace(f.Path))
        .Select(f => (ISearchResult)new StartupPanelResultItem(f.Path, f.Name));
}
