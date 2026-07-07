using System.IO;
using System.Reflection;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Settings;
using SwiftList.App.ViewModels.Settings.Plugins;
using SwiftList.Core;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;
using SwiftList.PluginSdk.Registries;

namespace SwiftList.App.Helpers;

public static class PluginLoaderHelper
{
    public static List<PluginInfoViewModel> BuildPluginList(UserSettings userSettings)
    {
        var result = new List<PluginInfoViewModel>();
        var manager = PluginManager.Instance;
        var disabledSet = new HashSet<string>(userSettings.DisabledPluginComponents);

        var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        if (!Directory.Exists(pluginsDir))
        {
            return result;
        }

        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.StartsWith(pluginsDir, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var assembly in loadedAssemblies)
        {
            var dllName = Path.GetFileName(assembly.Location);
            if (dllName.Equals("PluginSdk.dll", StringComparison.OrdinalIgnoreCase))
                continue;

            var sdkVersion = "1.0.0";
            var referencedSdk = assembly.GetReferencedAssemblies()
                .FirstOrDefault(r => r.Name != null && r.Name.Equals("PluginSdk", StringComparison.OrdinalIgnoreCase));
            if (referencedSdk != null && referencedSdk.Version != null)
            {
                sdkVersion = referencedSdk.Version.ToString(3);
            }

            var pluginName = Path.GetFileNameWithoutExtension(dllName);
            var pluginVersion = assembly.GetName().Version?.ToString(3) ?? "1.0.0";

            var pluginType = assembly.GetTypes().FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
            IPlugin? pluginInstance = null;
            if (pluginType != null)
            {
                pluginInstance = manager.Plugins.FirstOrDefault(p => p.GetType() == pluginType);
                if (pluginInstance != null)
                {
                    pluginName = pluginInstance.Name;

                }
            }

            if (pluginInstance == null)
            {
                pluginName = FallbackPluginName(assembly, pluginName);
            }

            var components = new List<PluginComponentViewModel>();
            if (pluginInstance != null)
            {
                components = BuildComponents(pluginInstance, dllName, manager, disabledSet);
            }
            else
            {
                AddAssemblyProviders(components, assembly, dllName, manager, disabledSet);
            }

            var configFields = new List<PluginConfigFieldViewModel>();
            TryLoadConfigFields(assembly, dllName, pluginInstance, userSettings, configFields);

            result.Add(new PluginInfoViewModel(pluginName, pluginVersion, dllName, sdkVersion, components, configFields));
        }

        return result;
    }

    private static string FallbackPluginName(Assembly assembly, string defaultName)
    {
        var firstAliasProv = AliasProviderRegistry.GetAllProviders().FirstOrDefault(p => p.GetType().Assembly == assembly);
        if (firstAliasProv != null && !string.IsNullOrWhiteSpace(firstAliasProv.Name)) return firstAliasProv.Name;

        var firstPathCol = ActivePathCollectorRegistry.GetAllCollectors().FirstOrDefault(p => p.GetType().Assembly == assembly);
        if (firstPathCol != null && !string.IsNullOrWhiteSpace(firstPathCol.Name)) return firstPathCol.Name;

        var firstAdapter = FileDialogAdapterRegistry.GetAllAdapters().FirstOrDefault(p => p.GetType().Assembly == assembly);
        if (firstAdapter != null && !string.IsNullOrWhiteSpace(firstAdapter.Name)) return firstAdapter.Name;

        var firstInlineAdapter = InlineSearchAdapterRegistry.GetAllAdapters().FirstOrDefault(p => p.GetType().Assembly == assembly);
        if (firstInlineAdapter != null && !string.IsNullOrWhiteSpace(firstInlineAdapter.Name)) return firstInlineAdapter.Name;

        return defaultName;
    }

    private static void TryLoadConfigFields(Assembly assembly, string dllName, IPlugin? pluginInstance, UserSettings userSettings, List<PluginConfigFieldViewModel> configFields)
    {
        try
        {
            var configurableType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IConfigurable).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
            if (configurableType != null)
            {
                IConfigurable? configurableInstance = null;
                if (pluginInstance != null && configurableType.IsAssignableFrom(pluginInstance.GetType()))
                {
                    configurableInstance = (IConfigurable)pluginInstance;
                }
                else
                {
                    configurableInstance = Activator.CreateInstance(configurableType) as IConfigurable;
                }

                if (configurableInstance != null)
                {
                    var schema = configurableInstance.GetConfigSchema();
                    if (schema != null && schema.Fields != null)
                    {
                        var pluginId = Path.GetFileNameWithoutExtension(dllName);
                        foreach (var field in schema.Fields)
                        {
                            configFields.Add(new PluginConfigFieldViewModel(pluginId, field, userSettings));
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static List<PluginComponentViewModel> BuildComponents(IPlugin plugin, string dllName, PluginManager manager, HashSet<string> disabledSet)
    {
        var components = new List<PluginComponentViewModel>();
        var assembly = plugin.GetType().Assembly;

        foreach (var reg in manager.AllActions.Where(r => r.Plugin == plugin))
        {
            var id = MakeId(dllName, PluginComponentType.Action, reg.Action.Id);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.Action, reg.Action.DisplayName, !disabledSet.Contains(id)));
        }

        AddAssemblyProviders(components, assembly, dllName, manager, disabledSet);
        return components;
    }

    private static void AddAssemblyProviders(List<PluginComponentViewModel> components, Assembly assembly, string dllName, PluginManager manager, HashSet<string> disabledSet)
    {
        foreach (var prov in AliasProviderRegistry.GetAllProviders().Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.AliasProvider, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.AliasProvider, string.IsNullOrWhiteSpace(prov.Name) ? prov.GetType().Name : prov.Name, !disabledSet.Contains(id)));
        }
        foreach (var prov in ActivePathCollectorRegistry.GetAllCollectors().Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.ActivePathCollector, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.ActivePathCollector, string.IsNullOrWhiteSpace(prov.Name) ? prov.GetType().Name : prov.Name, !disabledSet.Contains(id)));
        }
        foreach (var prov in FileDialogAdapterRegistry.GetAllAdapters().Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.FileDialogAdapter, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.FileDialogAdapter, string.IsNullOrWhiteSpace(prov.Name) ? prov.GetType().Name : prov.Name, !disabledSet.Contains(id)));
        }
        foreach (var prov in InlineSearchAdapterRegistry.GetAllAdapters().Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.InlineSearchAdapter, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.InlineSearchAdapter, string.IsNullOrWhiteSpace(prov.Name) ? prov.GetType().Name : prov.Name, !disabledSet.Contains(id)));
        }
        foreach (var prov in manager.AllInstantResultProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.InstantProvider, prov.Id);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.InstantProvider, prov.Name, !disabledSet.Contains(id)));
        }
        foreach (var prov in manager.AllSearchableItemProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.SearchableItemProvider, prov.Id);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.SearchableItemProvider, prov.Name, !disabledSet.Contains(id)));
        }
        foreach (var prov in manager.AllDynamicProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.DynamicProvider, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.DynamicProvider, prov.GroupName, !disabledSet.Contains(id)));
        }
        foreach (var prov in manager.AllQuickNavigationProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.QuickNavigationProvider, prov.GetType().Name);
            var displayName = TranslationService.Get("Plugins_Comp_QuickNavigationProvider") ?? "快捷导航";
            components.Add(new PluginComponentViewModel(id, PluginComponentType.QuickNavigationProvider, displayName, !disabledSet.Contains(id)));
        }
        foreach (var prov in manager.AllSidebarFilterProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var index = 0;
            foreach (var group in prov.GetFilterGroups())
            {
                var id = MakeId(dllName, PluginComponentType.FilterProvider, $"{prov.GetType().Name}_{index}");
                components.Add(new PluginComponentViewModel(id, PluginComponentType.FilterProvider, group.Header, !disabledSet.Contains(id)));
                index++;
            }
        }
        foreach (var prov in manager.AllResultColumnProviders.Where(p => p.GetType().Assembly == assembly))
        {
            foreach (var col in prov.GetColumns())
            {
                var id = MakeId(dllName, PluginComponentType.ColumnProvider, col.ColumnId);
                components.Add(new PluginComponentViewModel(id, PluginComponentType.ColumnProvider, col.HeaderText, !disabledSet.Contains(id)));
            }
        }
        foreach (var prov in manager.AllFilePreviewProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.FilePreviewProvider, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.FilePreviewProvider, prov.Name, !disabledSet.Contains(id)));
        }
        foreach (var prov in manager.AllThumbnailProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.ThumbnailProvider, prov.GetType().Name);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.ThumbnailProvider, prov.Name, !disabledSet.Contains(id)));
        }
        foreach (var prov in manager.AllQueryTokenProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.QueryTokenProvider, prov.Id);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.QueryTokenProvider, prov.Name, !disabledSet.Contains(id)));
        }
        foreach (var prov in manager.AllStartupPanelTabProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.StartupPanelTabProvider, prov.Id);
            components.Add(new PluginComponentViewModel(id, PluginComponentType.StartupPanelTabProvider, prov.Name, !disabledSet.Contains(id)));
        }
        foreach (var prov in manager.AllTranslationProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.TranslationProvider, prov.GetType().Name);
            var displayName = prov.SupportedCultures.Count > 0
                ? string.Join(", ", prov.SupportedCultures.Select(LanguageOption.GetLanguageDisplayName))
                : prov.Name;
            components.Add(new PluginComponentViewModel(id, PluginComponentType.TranslationProvider, displayName, true));
        }
        foreach (var prov in manager.AllThemeProviders.Where(p => p.GetType().Assembly == assembly))
        {
            var id = MakeId(dllName, PluginComponentType.ThemeProvider, prov.GetType().Name);
            var themes = prov.GetThemes().ToList();
            var displayName = themes.Count > 0
                ? string.Join(", ", themes.Select(t => t.DisplayName))
                : prov.Name;
            components.Add(new PluginComponentViewModel(id, PluginComponentType.ThemeProvider, displayName, true));
        }
    }

    private static string MakeId(string dllName, PluginComponentType type, string name) => $"{dllName}::{type}::{name}";
}
