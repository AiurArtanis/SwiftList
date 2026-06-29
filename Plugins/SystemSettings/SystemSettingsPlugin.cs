using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.SystemSettings;

public class SystemSettingsPlugin : IAction
{
    public string Name => TranslationService.Get("SystemSettings_PluginName");

    public IEnumerable<ISearchResultAction> GetActions() => Array.Empty<ISearchResultAction>();

    public IEnumerable<IDynamicActionProvider> GetDynamicProviders() => Array.Empty<IDynamicActionProvider>();
}
