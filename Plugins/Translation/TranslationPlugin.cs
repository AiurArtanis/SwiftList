using SwiftList.PluginSdk;

namespace SwiftList.Plugins.Translation;

public class TranslationPlugin : IActionPlugin
{
    public string Name => TranslationService.Get("Translation_PluginName");

    public IEnumerable<ISearchResultAction> GetActions() => Array.Empty<ISearchResultAction>();

    public IEnumerable<IDynamicActionProvider> GetDynamicProviders() => Array.Empty<IDynamicActionProvider>();
}
