using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.ProcessManager;

public class ProcessManagerPlugin : IPlugin
{
    public string Name => TranslationService.Get("ProcessManager_PluginName");
}
