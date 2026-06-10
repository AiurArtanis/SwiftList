using SwiftList.PluginSdk;

namespace SwiftList.Plugins.WebSearch;

public class WebSearchPlugin : IActionPlugin
{
    public string Name => TranslationService.Get("WebSearch_PluginName");

    public IEnumerable<ISearchResultAction> GetActions() => Array.Empty<ISearchResultAction>();

    public IEnumerable<IDynamicActionProvider> GetDynamicProviders() => Array.Empty<IDynamicActionProvider>();
}
