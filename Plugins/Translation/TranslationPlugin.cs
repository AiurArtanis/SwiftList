using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.Translation;

public class TranslationPlugin : IPlugin
{
    public string Name => TranslationService.Get("Translation_PluginName");
}
