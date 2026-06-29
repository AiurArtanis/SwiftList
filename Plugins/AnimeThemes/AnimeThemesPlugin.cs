using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.AnimeThemes;

public class AnimeThemesPlugin : IAction
{
    public string Name => TranslationService.Get("AnimeThemes_PluginName");

    public IEnumerable<ISearchResultAction> GetActions() => Array.Empty<ISearchResultAction>();

    public IEnumerable<IDynamicActionProvider> GetDynamicProviders() => Array.Empty<IDynamicActionProvider>();
}
