using System.IO;
using System.Reflection;
using SwiftList.Core;

namespace SwiftList.App.Services.PluginManagerCore;

/// <summary>
/// Scans the <c>Plugins/</c> directory for DLL assemblies and registers every
/// recognised <see cref="PluginSdk.IActionPlugin"/>, <see cref="IAliasProvider"/>,
/// <see cref="PluginSdk.IInstantResultProvider"/>, <see cref="PluginSdk.ISidebarFilterProvider"/>,
/// <see cref="PluginSdk.IResultColumnProvider"/> and <see cref="PluginSdk.ITranslationProvider"/>.
/// </summary>
internal static class PluginLoader
{
    /// <summary>
    /// Discovers and loads all plugin DLLs, delegating registration back to
    /// <paramref name="registry"/> via the supplied callbacks.
    /// </summary>
    internal static void Load(PluginRegistry registry)
    {
        try
        {
            var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            if (!Directory.Exists(pluginsDir))
                Directory.CreateDirectory(pluginsDir);

            foreach (var dllFile in Directory.GetFiles(pluginsDir, "*.dll"))
            {
                TryLoadAssembly(dllFile, registry);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[PluginManager] Error while loading plugins: {ex.Message}", LogLevel.Error);
        }

        // TranslationManager is reloaded explicitly in App.xaml.cs after all plugins are loaded,
        // to avoid a circular Lazy<T> initialization between PluginManager and TranslationManager.
    }

    private static void TryLoadAssembly(string dllFile, PluginRegistry registry)
    {
        var fileName = Path.GetFileName(dllFile);
        try
        {
            var assembly = Assembly.LoadFrom(dllFile);
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsInterface || type.IsAbstract)
                    continue;

                if (typeof(PluginSdk.IActionPlugin).IsAssignableFrom(type))
                {
                    var plugin = (PluginSdk.IActionPlugin)Activator.CreateInstance(type)!;
                    registry.RegisterPlugin(plugin);
                    Logger.Log($"[PluginManager] Loaded action plugin: '{type.Name}' (v{plugin.Version}) from {fileName}");
                }

                if (typeof(PluginSdk.IAliasProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.IAliasProvider)Activator.CreateInstance(type)!;
                    AliasProviderRegistry.Register(provider);
                    Logger.Log($"[PluginManager] Loaded alias provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.IInstantResultProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.IInstantResultProvider)Activator.CreateInstance(type)!;
                    registry.AddInstantResultProvider(provider);
                    Logger.Log($"[PluginManager] Loaded instant result provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.ISearchableItemProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.ISearchableItemProvider)Activator.CreateInstance(type)!;
                    registry.AddSearchableItemProvider(provider);
                    Logger.Log($"[PluginManager] Loaded searchable item provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.ISidebarFilterProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.ISidebarFilterProvider)Activator.CreateInstance(type)!;
                    registry.AddSidebarFilterProvider(provider);
                    Logger.Log($"[PluginManager] Loaded sidebar filter provider from {fileName}");
                }

                if (typeof(PluginSdk.IResultColumnProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.IResultColumnProvider)Activator.CreateInstance(type)!;
                    registry.AddResultColumnProvider(provider);
                    Logger.Log($"[PluginManager] Loaded result column provider from {fileName}");
                }

                if (typeof(PluginSdk.ITranslationProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.ITranslationProvider)Activator.CreateInstance(type)!;
                    registry.AddTranslationProvider(provider);
                    Logger.Log($"[PluginManager] Loaded translation provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.IThemeProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.IThemeProvider)Activator.CreateInstance(type)!;
                    registry.AddThemeProvider(provider);
                    Logger.Log($"[PluginManager] Loaded theme provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.IActivePathCollector).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.IActivePathCollector)Activator.CreateInstance(type)!;
                    registry.AddActivePathCollector(provider);
                    Logger.Log($"[PluginManager] Loaded active path collector: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.IFilePreviewProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.IFilePreviewProvider)Activator.CreateInstance(type)!;
                    registry.AddFilePreviewProvider(provider);
                    Logger.Log($"[PluginManager] Loaded file preview provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.IFileDialogAdapter).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.IFileDialogAdapter)Activator.CreateInstance(type)!;
                    PluginSdk.FileDialogAdapterRegistry.Register(provider);
                    Logger.Log($"[PluginManager] Loaded file dialog adapter: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.IInlineSearchAdapter).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.IInlineSearchAdapter)Activator.CreateInstance(type)!;
                    PluginSdk.InlineSearchAdapterRegistry.Register(provider);
                    Logger.Log($"[PluginManager] Loaded inline search adapter: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.IQuickNavigationProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.IQuickNavigationProvider)Activator.CreateInstance(type)!;
                    registry.AddQuickNavigationProvider(provider);
                    Logger.Log($"[PluginManager] Loaded quick navigation provider: '{type.Name}' from {fileName}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[PluginManager] Failed to load assembly {fileName}: {ex.Message}", LogLevel.Error);
        }
    }
}
