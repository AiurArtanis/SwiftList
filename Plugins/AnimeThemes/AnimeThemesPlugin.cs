using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.AnimeThemes;

public class AnimeThemesPlugin : IPlugin
{
    public string Name => TranslationService.Get("AnimeThemes_PluginName");
    public string Description => TranslationService.Get("AnimeThemes_PluginDesc");
}
