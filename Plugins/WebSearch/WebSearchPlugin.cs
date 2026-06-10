using System.Collections.Generic;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.WebSearch
{
    public class WebSearchPlugin : IActionPlugin
    {
        public string Name => TranslationService.Get("WebSearch_PluginName");

        public IEnumerable<ISearchResultAction> GetActions()
        {
            return System.Array.Empty<ISearchResultAction>();
        }

        public IEnumerable<IDynamicActionProvider> GetDynamicProviders()
        {
            return System.Array.Empty<IDynamicActionProvider>();
        }
    }
}
