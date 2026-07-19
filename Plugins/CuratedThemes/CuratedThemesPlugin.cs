using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CuratedThemes;

public class CuratedThemesPlugin : IPlugin
{
    public string Name => TranslationService.Get("CuratedThemes_PluginName");
    public string Description => TranslationService.Get("CuratedThemes_PluginDesc");
}
