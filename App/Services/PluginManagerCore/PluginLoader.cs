using System;
using System.IO;
using System.Reflection;
using SwiftList.Core;
using SwiftList.PluginSdk;

namespace SwiftList.App.Services.PluginManagerCore
{
    /// <summary>
    /// Scans the <c>Plugins/</c> directory for DLL assemblies and registers every
    /// recognised <see cref="IActionPlugin"/>, <see cref="IAliasProvider"/>,
    /// <see cref="IInstantResultProvider"/>, <see cref="ISidebarFilterProvider"/>,
    /// <see cref="IResultColumnProvider"/> and <see cref="ITranslationProvider"/>.
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
                Logger.Log($"[PluginManager] Error while loading plugins: {ex.Message}");
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

                    if (typeof(IActionPlugin).IsAssignableFrom(type))
                    {
                        var plugin = (IActionPlugin)Activator.CreateInstance(type)!;
                        registry.RegisterPlugin(plugin);
                        Logger.Log($"[PluginManager] Loaded action plugin: '{type.Name}' (v{plugin.Version}) from {fileName}");
                    }

                    if (typeof(IAliasProvider).IsAssignableFrom(type))
                    {
                        var provider = (IAliasProvider)Activator.CreateInstance(type)!;
                        AliasProviderRegistry.Register(provider);
                        Logger.Log($"[PluginManager] Loaded alias provider: '{type.Name}' ({provider.Id}) from {fileName}");
                    }

                    if (typeof(IInstantResultProvider).IsAssignableFrom(type))
                    {
                        var provider = (IInstantResultProvider)Activator.CreateInstance(type)!;
                        registry.AddInstantResultProvider(provider);
                        Logger.Log($"[PluginManager] Loaded instant result provider: '{type.Name}' from {fileName}");
                    }

                    if (typeof(ISidebarFilterProvider).IsAssignableFrom(type))
                    {
                        var provider = (ISidebarFilterProvider)Activator.CreateInstance(type)!;
                        registry.AddSidebarFilterProvider(provider);
                        Logger.Log($"[PluginManager] Loaded sidebar filter provider from {fileName}");
                    }

                    if (typeof(IResultColumnProvider).IsAssignableFrom(type))
                    {
                        var provider = (IResultColumnProvider)Activator.CreateInstance(type)!;
                        registry.AddResultColumnProvider(provider);
                        Logger.Log($"[PluginManager] Loaded result column provider from {fileName}");
                    }

                    if (typeof(ITranslationProvider).IsAssignableFrom(type))
                    {
                        var provider = (ITranslationProvider)Activator.CreateInstance(type)!;
                        registry.AddTranslationProvider(provider);
                        Logger.Log($"[PluginManager] Loaded translation provider: '{type.Name}' from {fileName}");
                    }

                    if (typeof(IThemeProvider).IsAssignableFrom(type))
                    {
                        var provider = (IThemeProvider)Activator.CreateInstance(type)!;
                        registry.AddThemeProvider(provider);
                        Logger.Log($"[PluginManager] Loaded theme provider: '{type.Name}' from {fileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[PluginManager] Failed to load assembly {fileName}: {ex.Message}");
            }
        }
    }
}
