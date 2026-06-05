using System;
using System.IO;
using System.Reflection;
using SwiftList.Core;

namespace SwiftList.App.Services.PluginManagerCore
{
    /// <summary>
    /// Scans the <c>Plugins/</c> directory for DLL assemblies and registers every
    /// recognised <see cref="SwiftList.PluginSdk.IActionPlugin"/>, <see cref="IAliasProvider"/>,
    /// <see cref="SwiftList.PluginSdk.IInstantResultProvider"/>, <see cref="SwiftList.PluginSdk.ISidebarFilterProvider"/>,
    /// <see cref="SwiftList.PluginSdk.IResultColumnProvider"/> and <see cref="SwiftList.PluginSdk.ITranslationProvider"/>.
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
                string pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
                if (!Directory.Exists(pluginsDir))
                    Directory.CreateDirectory(pluginsDir);

                foreach (string dllFile in Directory.GetFiles(pluginsDir, "*.dll"))
                {
                    TryLoadAssembly(dllFile, registry);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[PluginManager] Error while loading plugins: {ex.Message}", SwiftList.Core.LogLevel.Error);
            }

            // TranslationManager is reloaded explicitly in App.xaml.cs after all plugins are loaded,
            // to avoid a circular Lazy<T> initialization between PluginManager and TranslationManager.
        }

        private static void TryLoadAssembly(string dllFile, PluginRegistry registry)
        {
            string fileName = Path.GetFileName(dllFile);
            try
            {
                Assembly assembly = Assembly.LoadFrom(dllFile);
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.IsInterface || type.IsAbstract)
                        continue;

                    if (typeof(SwiftList.PluginSdk.IActionPlugin).IsAssignableFrom(type))
                    {
                        var plugin = (SwiftList.PluginSdk.IActionPlugin)Activator.CreateInstance(type)!;
                        registry.RegisterPlugin(plugin);
                        Logger.Log($"[PluginManager] Loaded action plugin: '{type.Name}' (v{plugin.Version}) from {fileName}");
                    }

                    if (typeof(SwiftList.PluginSdk.IAliasProvider).IsAssignableFrom(type))
                    {
                        var provider = (SwiftList.PluginSdk.IAliasProvider)Activator.CreateInstance(type)!;
                        AliasProviderRegistry.Register(provider);
                        Logger.Log($"[PluginManager] Loaded alias provider: '{type.Name}' from {fileName}");
                    }

                    if (typeof(SwiftList.PluginSdk.IInstantResultProvider).IsAssignableFrom(type))
                    {
                        var provider = (SwiftList.PluginSdk.IInstantResultProvider)Activator.CreateInstance(type)!;
                        registry.AddInstantResultProvider(provider);
                        Logger.Log($"[PluginManager] Loaded instant result provider: '{type.Name}' from {fileName}");
                    }

                    if (typeof(SwiftList.PluginSdk.ISidebarFilterProvider).IsAssignableFrom(type))
                    {
                        var provider = (SwiftList.PluginSdk.ISidebarFilterProvider)Activator.CreateInstance(type)!;
                        registry.AddSidebarFilterProvider(provider);
                        Logger.Log($"[PluginManager] Loaded sidebar filter provider from {fileName}");
                    }

                    if (typeof(SwiftList.PluginSdk.IResultColumnProvider).IsAssignableFrom(type))
                    {
                        var provider = (SwiftList.PluginSdk.IResultColumnProvider)Activator.CreateInstance(type)!;
                        registry.AddResultColumnProvider(provider);
                        Logger.Log($"[PluginManager] Loaded result column provider from {fileName}");
                    }

                    if (typeof(SwiftList.PluginSdk.ITranslationProvider).IsAssignableFrom(type))
                    {
                        var provider = (SwiftList.PluginSdk.ITranslationProvider)Activator.CreateInstance(type)!;
                        registry.AddTranslationProvider(provider);
                        Logger.Log($"[PluginManager] Loaded translation provider: '{type.Name}' from {fileName}");
                    }

                    if (typeof(SwiftList.PluginSdk.IThemeProvider).IsAssignableFrom(type))
                    {
                        var provider = (SwiftList.PluginSdk.IThemeProvider)Activator.CreateInstance(type)!;
                        registry.AddThemeProvider(provider);
                        Logger.Log($"[PluginManager] Loaded theme provider: '{type.Name}' from {fileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[PluginManager] Failed to load assembly {fileName}: {ex.Message}", SwiftList.Core.LogLevel.Error);
            }
        }
    }
}
