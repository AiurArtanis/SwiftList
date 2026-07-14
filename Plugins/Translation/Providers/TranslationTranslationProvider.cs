using System.Reflection;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.Translation.Providers;

public class TranslationTranslationProvider : ITranslationProvider
{
    public string Name => "Translation Plugin Translation Provider";

    public string Description => TranslationService.Get("Plugin_Comp_Desc_TranslationTranslationProvider");

    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(Assembly.GetExecutingAssembly());

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

            var dict = TranslationService.LoadEmbeddedTranslations(Assembly.GetExecutingAssembly(), cultureName, "Plugin");
            Cache[cultureName] = dict;
            return dict;
        }
    }
}
