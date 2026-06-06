using System;
using System.Collections.Generic;
using System.Linq;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.ListSearch
{
    public class ListSearchPlugin : IActionPlugin, ITranslationProvider
    {
        public string Name => TranslationService.Get("Plugins_ListSearchPluginName");

        string ITranslationProvider.Name => "ListSearch Translation Provider";

        public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(System.Reflection.Assembly.GetExecutingAssembly());

        private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object LockObj = new();

        public IReadOnlyDictionary<string, string> GetTranslations(string cultureName)
        {
            lock (LockObj)
            {
                if (Cache.TryGetValue(cultureName, out var cached))
                {
                    return cached;
                }

                var translations = TranslationService.LoadEmbeddedTranslations(System.Reflection.Assembly.GetExecutingAssembly(), cultureName, "Plugin");
                Cache[cultureName] = translations;
                return translations;
            }
        }

        public IEnumerable<ISearchResultAction> GetActions()
        {
            return Enumerable.Empty<ISearchResultAction>();
        }

        public IEnumerable<IDynamicActionProvider> GetDynamicProviders()
        {
            return Enumerable.Empty<IDynamicActionProvider>();
        }
    }
}
