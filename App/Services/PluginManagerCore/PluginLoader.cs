using System.IO;
using System.Reflection;
using SwiftList.Core;

namespace SwiftList.App.Services.PluginManagerCore;

/// <summary>
/// Scans the <c>Plugins/</c> directory for DLL assemblies and registers every
/// recognised <see cref="PluginSdk.Abstractions.Plugins.IPlugin"/>, <see cref="IAliasProvider"/>,
/// <see cref="PluginSdk.Abstractions.Plugins.IInstantResultProvider"/>, <see cref="PluginSdk.Abstractions.Plugins.ISidebarFilterProvider"/>,
/// <see cref="PluginSdk.Abstractions.Plugins.IResultColumnProvider"/> and <see cref="PluginSdk.Abstractions.Plugins.ITranslationProvider"/>.
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

                if (typeof(PluginSdk.Abstractions.Plugins.IPlugin).IsAssignableFrom(type))
                {
                    var plugin = (PluginSdk.Abstractions.Plugins.IPlugin)Activator.CreateInstance(type)!;
                    registry.RegisterPlugin(plugin);
                    var pluginVer = assembly.GetName().Version?.ToString(3) ?? "1.0.0";
                    Logger.Log($"[PluginManager] Loaded plugin: '{type.Name}' (v{pluginVer}) from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IAliasProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IAliasProvider)Activator.CreateInstance(type)!;
                    AliasProviderRegistry.Register(provider);
                    Logger.Log($"[PluginManager] Loaded alias provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IInstantResultProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IInstantResultProvider)Activator.CreateInstance(type)!;
                    registry.AddInstantResultProvider(provider);
                    Logger.Log($"[PluginManager] Loaded instant result provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.ISearchableItemProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.ISearchableItemProvider)Activator.CreateInstance(type)!;
                    registry.AddSearchableItemProvider(provider);
                    Logger.Log($"[PluginManager] Loaded searchable item provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.ISidebarFilterProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.ISidebarFilterProvider)Activator.CreateInstance(type)!;
                    registry.AddSidebarFilterProvider(provider);
                    Logger.Log($"[PluginManager] Loaded sidebar filter provider from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IResultColumnProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IResultColumnProvider)Activator.CreateInstance(type)!;
                    registry.AddResultColumnProvider(provider);
                    Logger.Log($"[PluginManager] Loaded result column provider from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.ITranslationProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.ITranslationProvider)Activator.CreateInstance(type)!;
                    registry.AddTranslationProvider(provider);
                    Logger.Log($"[PluginManager] Loaded translation provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IThemeProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IThemeProvider)Activator.CreateInstance(type)!;
                    registry.AddThemeProvider(provider);
                    Logger.Log($"[PluginManager] Loaded theme provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IActivePathCollector).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IActivePathCollector)Activator.CreateInstance(type)!;
                    registry.AddActivePathCollector(provider);
                    Logger.Log($"[PluginManager] Loaded active path collector: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IFilePreviewProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IFilePreviewProvider)Activator.CreateInstance(type)!;
                    registry.AddFilePreviewProvider(provider);
                    Logger.Log($"[PluginManager] Loaded file preview provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IFileDialogAdapter).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IFileDialogAdapter)Activator.CreateInstance(type)!;
                    PluginSdk.Registries.FileDialogAdapterRegistry.Register(provider);
                    Logger.Log($"[PluginManager] Loaded file dialog adapter: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IInlineSearchAdapter).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IInlineSearchAdapter)Activator.CreateInstance(type)!;
                    PluginSdk.Registries.InlineSearchAdapterRegistry.Register(provider);
                    Logger.Log($"[PluginManager] Loaded inline search adapter: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IQuickNavigationProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IQuickNavigationProvider)Activator.CreateInstance(type)!;
                    registry.AddQuickNavigationProvider(provider);
                    Logger.Log($"[PluginManager] Loaded quick navigation provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IThumbnailProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IThumbnailProvider)Activator.CreateInstance(type)!;
                    registry.AddThumbnailProvider(provider);
                    Logger.Log($"[PluginManager] Loaded thumbnail provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IQueryTokenProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IQueryTokenProvider)Activator.CreateInstance(type)!;
                    registry.AddQueryTokenProvider(provider);
                    Logger.Log($"[PluginManager] Loaded query token provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IStartupPanelTabProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IStartupPanelTabProvider)Activator.CreateInstance(type)!;
                    registry.AddStartupPanelTabProvider(provider);
                    Logger.Log($"[PluginManager] Loaded startup panel tab provider: '{type.Name}' from {fileName}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[PluginManager] Failed to load assembly {fileName}: {ex.Message}", LogLevel.Error);
        }
    }
}
