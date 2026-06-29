using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.Translation;

public class TranslationPlugin : IAction
{
    public string Name => TranslationService.Get("Translation_PluginName");

    public IEnumerable<ISearchResultAction> GetActions() => Array.Empty<ISearchResultAction>();

    public IEnumerable<IDynamicActionProvider> GetDynamicProviders() => Array.Empty<IDynamicActionProvider>();
}
