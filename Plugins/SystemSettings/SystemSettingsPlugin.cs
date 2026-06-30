using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.SystemSettings;

public class SystemSettingsPlugin : IPlugin
{
    public string Name => TranslationService.Get("SystemSettings_PluginName");
}
