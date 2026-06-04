using System;
using System.IO;
using System.Reflection;
using SwiftList.Core;
using SwiftList.PluginSdk;

namespace SwiftList.Service
{
    public static class ServicePluginLoader
    {
        public static void LoadPlugins()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string pluginsDir = Path.Combine(baseDir, "Plugins");

                Logger.Log($"[ServicePluginLoader] Scanning for alias plugins in: {pluginsDir}");

                if (!Directory.Exists(pluginsDir))
                {
                    Directory.CreateDirectory(pluginsDir);
                    return;
                }

                string[] dllFiles = Directory.GetFiles(pluginsDir, "*.dll");
                foreach (string dllFile in dllFiles)
                {
                    try
                    {
                        Assembly assembly = Assembly.LoadFrom(dllFile);
                        foreach (Type type in assembly.GetTypes())
                        {
                            if (type.IsInterface || type.IsAbstract)
                                continue;

                            if (typeof(IAliasProvider).IsAssignableFrom(type))
                            {
                                IAliasProvider provider = (IAliasProvider)Activator.CreateInstance(type)!;
                                AliasProviderRegistry.Register(provider);
                                Logger.Log($"[ServicePluginLoader] Loaded alias provider: '{type.Name}' ({provider.Id}) from {Path.GetFileName(dllFile)}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[ServicePluginLoader] Failed to load plugin assembly {Path.GetFileName(dllFile)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[ServicePluginLoader] Error while loading plugins: {ex.Message}");
            }
        }
    }
}
