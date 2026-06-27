using System.Reflection;
using SwiftList.PluginSdk;

namespace SwiftList.Core.Services;

public static class ServicePluginLoader
{
    public static void LoadForService() => LoadPlugins(loadHookPlugins: false);

    public static void LoadForHook() => LoadPlugins(loadHookPlugins: true);

    private static void LoadPlugins(bool loadHookPlugins)
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var pluginsDir = Path.Combine(baseDir, "Plugins");

            Logger.Log($"[ServicePluginLoader] Scanning plugins in: {pluginsDir}");

            if (!Directory.Exists(pluginsDir))
            {
                Directory.CreateDirectory(pluginsDir);
                return;
            }

            var translationProviders = new List<ITranslationProvider>();
            var aliasProviders = new List<IAliasProvider>();

            var dllFiles = Directory.GetFiles(pluginsDir, "*.dll");
            foreach (var dllFile in dllFiles)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dllFile);
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsInterface || type.IsAbstract)
                            continue;

                        var isAliasProvider = typeof(IAliasProvider).IsAssignableFrom(type);
                        if (!loadHookPlugins && !isAliasProvider)
                            continue;

                        if (typeof(IAliasProvider).IsAssignableFrom(type))
                        {
                            var provider = (IAliasProvider)Activator.CreateInstance(type)!;
                            aliasProviders.Add(provider);
                        }

                        if ((loadHookPlugins || isAliasProvider) && typeof(ITranslationProvider).IsAssignableFrom(type))
                        {
                            var provider = (ITranslationProvider)Activator.CreateInstance(type)!;
                            translationProviders.Add(provider);
                            if (loadHookPlugins)
                                Logger.Log($"[ServicePluginLoader] Loaded translation provider: '{type.Name}' from {Path.GetFileName(dllFile)}");
                        }

                        if (loadHookPlugins && typeof(IActivePathCollector).IsAssignableFrom(type))
                        {
                            var provider = (IActivePathCollector)Activator.CreateInstance(type)!;
                            ActivePathCollectorRegistry.Register(provider);
                            Logger.Log($"[ServicePluginLoader] Loaded active path collector: '{type.Name}' from {Path.GetFileName(dllFile)}");
                        }

                        if (loadHookPlugins && typeof(IFileDialogAdapter).IsAssignableFrom(type))
                        {
                            var provider = (IFileDialogAdapter)Activator.CreateInstance(type)!;
                            FileDialogAdapterRegistry.Register(provider);
                            Logger.Log($"[ServicePluginLoader] Loaded file dialog adapter: '{type.Name}' from {Path.GetFileName(dllFile)}");
                        }

                        if (loadHookPlugins && typeof(IInlineSearchAdapter).IsAssignableFrom(type))
                        {
                            var provider = (IInlineSearchAdapter)Activator.CreateInstance(type)!;
                            InlineSearchAdapterRegistry.Register(provider);
                            Logger.Log($"[ServicePluginLoader] Loaded inline search adapter: '{type.Name}' from {Path.GetFileName(dllFile)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ServicePluginLoader] Failed to load plugin assembly {Path.GetFileName(dllFile)}: {ex.Message}", LogLevel.Error);
                }
            }

            // Initialize TranslationService LookupFunc in the service process using the loaded translation providers
            var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var cultureName = System.Globalization.CultureInfo.CurrentUICulture.Name;
            foreach (var provider in translationProviders)
            {
                try
                {
                    var dict = provider.GetTranslations(cultureName);
                    if (dict != null)
                    {
                        foreach (var kvp in dict)
                        {
                            translations[kvp.Key] = kvp.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ServicePluginLoader] Failed to load translations from '{provider.Name}': {ex.Message}", LogLevel.Error);
                }
            }

            TranslationService.LookupFunc = key => translations.TryGetValue(key, out var val) ? val : $"[{key}]";

            if (loadHookPlugins)
            {
                // Wire up FilterFuncs so the hook process respects enabled/disabled state.
                // The lambda reads UserSettings.Load() (cached) on every call, so after a
                // ReloadSettings command triggers UserSettings.ForceReload() the next adapter
                // lookup will automatically reflect the new disabled-components list.
                InlineSearchAdapterRegistry.FilterFunc = a => IsComponentEnabled(a);
                FileDialogAdapterRegistry.FilterFunc = a => IsComponentEnabled(a);
                ActivePathCollectorRegistry.FilterFunc = a => IsComponentEnabled(a);
            }

            // Now register alias providers (this will trigger provider.Name evaluation)
            foreach (var provider in aliasProviders)
            {
                AliasProviderRegistry.Register(provider);
                Logger.Log($"[ServicePluginLoader] Loaded alias provider: '{provider.GetType().Name}'");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ServicePluginLoader] Error while loading plugins: {ex.Message}", LogLevel.Error);
        }
    }

    private static bool IsComponentEnabled(object obj)
    {
        try
        {
            var dllName = Path.GetFileName(obj.GetType().Assembly.Location);
            var typeName = obj.GetType().Name;
            var settings = UserSettings.Load();

            // Match the same ID formats used by App's ComponentFilter / MakeId helper
            var idInlineSearch = $"{dllName}::InlineSearchAdapter::{typeName}";
            var idFileDialog = $"{dllName}::FileDialogAdapter::{typeName}";
            var idPathCollect = $"{dllName}::ActivePathCollector::{typeName}";
            var idAlias = $"{dllName}::AliasProvider::{typeName}";

            return !settings.DisabledPluginComponents.Contains(idInlineSearch, StringComparer.OrdinalIgnoreCase)
                && !settings.DisabledPluginComponents.Contains(idFileDialog, StringComparer.OrdinalIgnoreCase)
                && !settings.DisabledPluginComponents.Contains(idPathCollect, StringComparer.OrdinalIgnoreCase)
                && !settings.DisabledPluginComponents.Contains(idAlias, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }
}
