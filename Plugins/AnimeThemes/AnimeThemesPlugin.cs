using SwiftList.PluginSdk;

namespace SwiftList.Plugins.AnimeThemes;

public class AnimeThemesPlugin : IActionPlugin
{
    public string Name => TranslationService.Get("AnimeThemes_PluginName");

    public IEnumerable<ISearchResultAction> GetActions() => Array.Empty<ISearchResultAction>();

    public IEnumerable<IDynamicActionProvider> GetDynamicProviders() => Array.Empty<IDynamicActionProvider>();
}
