using System.Collections.Generic;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.Translation
{
    public class TranslationPlugin : IActionPlugin
    {
        public string Name => TranslationService.Get("Translation_PluginName");

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
