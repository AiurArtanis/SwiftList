using SwiftList.PluginSdk;

namespace SwiftList.Plugins.SystemSettings;

public class SystemSettingsPlugin : IActionPlugin
{
    public string Name => TranslationService.Get("SystemSettings_PluginName");

    public IEnumerable<ISearchResultAction> GetActions() => Array.Empty<ISearchResultAction>();

    public IEnumerable<IDynamicActionProvider> GetDynamicProviders() => Array.Empty<IDynamicActionProvider>();
}
