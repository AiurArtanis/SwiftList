using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Settings;
using SwiftList.Core;
using SwiftList.PluginSdk;

namespace SwiftList.App.ViewModels.Settings.Plugins
{
    /// <summary>
    /// ViewModel for the Plugin Management settings page.
    /// Loads installed plugins and exposes their sub-components with enable/disable toggles.
    /// </summary>
    public class PluginManagementViewModel : ViewModelBase
    {
        private readonly UserSettings _userSettings;

        public PluginManagementViewModel(UserSettings userSettings)
        {
            _userSettings = userSettings;
            Plugins = new ObservableCollection<PluginInfoViewModel>(BuildPluginList());
            ToggleExpandCommand = new RelayCommand<PluginInfoViewModel>(p =>
            {
                if (p != null)
                    p.IsExpanded = !p.IsExpanded;
            });

            // Dynamically refresh the plugin list when language changes to dynamically apply localized plugin names
            TranslationManager.Instance.PropertyChanged += (s, e) =>
            {
                var expandedStates = Plugins.ToDictionary(p => p.DllFileName, p => p.IsExpanded);
                var newList = BuildPluginList();
                Plugins.Clear();
                foreach (var p in newList)
                {
                    if (expandedStates.TryGetValue(p.DllFileName, out bool isExpanded))
                    {
                        p.IsExpanded = isExpanded;
                    }
                    Plugins.Add(p);
                }
                OnPropertyChanged(nameof(IsEmpty));
            };
        }

        public ObservableCollection<PluginInfoViewModel> Plugins { get; }

        public ICommand ToggleExpandCommand { get; }

        public bool IsEmpty => Plugins.Count == 0;

        public string HostSdkVersion { get; } = typeof(IActionPlugin).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        public void Save()
        {
            var disabledIds = Plugins
                .SelectMany(p => p.RawComponents)
                .Where(c => c.IsToggleable && !c.IsEnabled)
                .Select(c => c.ComponentId)
                .ToList();

            _userSettings.DisabledPluginComponents = disabledIds;
        }

        private List<PluginInfoViewModel> BuildPluginList()
        {
            var result = new List<PluginInfoViewModel>();
            var manager = PluginManager.Instance;
            var disabledSet = new HashSet<string>(_userSettings.DisabledPluginComponents);

            string pluginsDir = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            if (!Directory.Exists(pluginsDir))
            {
                return result;
            }

            // Find all loaded assemblies from the Plugins directory
            var loadedAssemblies = System.AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && a.Location.StartsWith(pluginsDir, System.StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var assembly in loadedAssemblies)
            {
                string dllName = Path.GetFileName(assembly.Location);

                // Skip the core SDK Contract DLL
                if (dllName.Equals("PluginSdk.dll", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                // Query referenced Plugin SDK version dynamically
                string sdkVersion = "1.0.0";
                var referencedSdk = assembly.GetReferencedAssemblies()
                    .FirstOrDefault(r => r.Name != null && r.Name.Equals("PluginSdk", System.StringComparison.OrdinalIgnoreCase));
                if (referencedSdk != null && referencedSdk.Version != null)
                {
                    sdkVersion = referencedSdk.Version.ToString(3); // e.g. "1.0.0"
                }

                // Determine display name:
                // 1. Try IActionPlugin Name (which automatically returns decentralized localized string)
                // 2. Fallback to raw DLL name without extension
                string pluginName = Path.GetFileNameWithoutExtension(dllName);
                string pluginVersion = assembly.GetName().Version?.ToString(3) ?? "1.0.0";

                var pluginType = assembly.GetTypes().FirstOrDefault(t => typeof(IActionPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                IActionPlugin? pluginInstance = null;
                if (pluginType != null)
                {
                    pluginInstance = manager.Plugins.FirstOrDefault(p => p.GetType() == pluginType);
                    if (pluginInstance != null)
                    {
                        pluginName = pluginInstance.Name;
                        pluginVersion = pluginInstance.Version;
                    }
                }

                // For pure DLL extensions (like PinyinAlias) that do not implement IActionPlugin,
                // we check if they have registered an IAliasProvider and query its localized Name.
                // Use GetAllProviders() so the name is found even when the provider is disabled.
                if (pluginInstance == null)
                {
                    var firstAliasProv = AliasProviderRegistry.GetAllProviders()
                        .FirstOrDefault(p => p.GetType().Assembly == assembly);
                    if (firstAliasProv != null && !string.IsNullOrWhiteSpace(firstAliasProv.Name))
                    {
                        pluginName = firstAliasProv.Name;
                    }
                    else
                    {
                        var firstPathCol = ActivePathCollectorRegistry.GetAllCollectors()
                            .FirstOrDefault(p => p.GetType().Assembly == assembly);
                        if (firstPathCol != null && !string.IsNullOrWhiteSpace(firstPathCol.Name))
                        {
                            pluginName = firstPathCol.Name;
                        }
                        else
                        {
                            var firstAdapter = FileDialogAdapterRegistry.GetAllAdapters()
                                .FirstOrDefault(p => p.GetType().Assembly == assembly);
                            if (firstAdapter != null && !string.IsNullOrWhiteSpace(firstAdapter.Name))
                            {
                                pluginName = firstAdapter.Name;
                            }
                            else
                            {
                                var firstInlineAdapter = InlineSearchAdapterRegistry.GetAllAdapters()
                                    .FirstOrDefault(p => p.GetType().Assembly == assembly);
                                if (firstInlineAdapter != null && !string.IsNullOrWhiteSpace(firstInlineAdapter.Name))
                                {
                                    pluginName = firstInlineAdapter.Name;
                                }
                            }
                        }
                    }
                }

                // Format metadata (simply use dllName, SDK version is put on the title card)
                string formattedDllSource = dllName;

                // If we have an IActionPlugin, build with it, otherwise build directly from the assembly reference
                var components = new List<PluginComponentViewModel>();
                if (pluginInstance != null)
                {
                    components = BuildComponents(pluginInstance, dllName, manager, disabledSet);
                }
                else
                {
                    AddAssemblyProviders(components, assembly, dllName, manager, disabledSet);
                }

                result.Add(new PluginInfoViewModel(pluginName, pluginVersion, formattedDllSource, sdkVersion, components));
            }

            return result;
        }

        private static void AddAssemblyProviders(
            List<PluginComponentViewModel> components,
            System.Reflection.Assembly assembly,
            string dllName,
            PluginManager manager,
            HashSet<string> disabledSet)
        {
            foreach (var prov in AliasProviderRegistry.GetAllProviders().Where(p => p.GetType().Assembly == assembly))
            {
                string id = MakeId(dllName, PluginComponentType.AliasProvider, prov.GetType().Name);
                components.Add(new PluginComponentViewModel(id, PluginComponentType.AliasProvider, string.IsNullOrWhiteSpace(prov.Name) ? prov.GetType().Name : prov.Name, !disabledSet.Contains(id)));
            }
            foreach (var prov in ActivePathCollectorRegistry.GetAllCollectors().Where(p => p.GetType().Assembly == assembly))
            {
                string id = MakeId(dllName, PluginComponentType.ActivePathCollector, prov.GetType().Name);
                components.Add(new PluginComponentViewModel(id, PluginComponentType.ActivePathCollector, string.IsNullOrWhiteSpace(prov.Name) ? prov.GetType().Name : prov.Name, !disabledSet.Contains(id)));
            }
            foreach (var prov in FileDialogAdapterRegistry.GetAllAdapters().Where(p => p.GetType().Assembly == assembly))
            {
                string id = MakeId(dllName, PluginComponentType.FileDialogAdapter, prov.GetType().Name);
                components.Add(new PluginComponentViewModel(id, PluginComponentType.FileDialogAdapter, string.IsNullOrWhiteSpace(prov.Name) ? prov.GetType().Name : prov.Name, !disabledSet.Contains(id)));
            }
            foreach (var prov in InlineSearchAdapterRegistry.GetAllAdapters().Where(p => p.GetType().Assembly == assembly))
            {
                string id = MakeId(dllName, PluginComponentType.InlineSearchAdapter, prov.GetType().Name);
                components.Add(new PluginComponentViewModel(id, PluginComponentType.InlineSearchAdapter, string.IsNullOrWhiteSpace(prov.Name) ? prov.GetType().Name : prov.Name, !disabledSet.Contains(id)));
            }
            foreach (var prov in manager.AllInstantResultProviders.Where(p => p.GetType().Assembly == assembly))
            {
                string id = MakeId(dllName, PluginComponentType.InstantProvider, prov.Id);
                components.Add(new PluginComponentViewModel(id, PluginComponentType.InstantProvider, prov.Name, !disabledSet.Contains(id)));
            }
            foreach (var prov in manager.AllDynamicProviders.Where(p => p.GetType().Assembly == assembly))
            {
                string id = MakeId(dllName, PluginComponentType.DynamicProvider, prov.GetType().Name);
                components.Add(new PluginComponentViewModel(id, PluginComponentType.DynamicProvider, prov.GroupName, !disabledSet.Contains(id)));
            }
            foreach (var prov in manager.AllSidebarFilterProviders.Where(p => p.GetType().Assembly == assembly))
            {
                int index = 0;
                foreach (var group in prov.GetFilterGroups())
                {
                    string id = MakeId(dllName, PluginComponentType.FilterProvider, $"{prov.GetType().Name}_{index}");
                    components.Add(new PluginComponentViewModel(id, PluginComponentType.FilterProvider, group.Header, !disabledSet.Contains(id)));
                    index++;
                }
            }
            foreach (var prov in manager.AllResultColumnProviders.Where(p => p.GetType().Assembly == assembly))
            {
                foreach (var col in prov.GetColumns())
                {
                    string id = MakeId(dllName, PluginComponentType.ColumnProvider, col.ColumnId);
                    components.Add(new PluginComponentViewModel(id, PluginComponentType.ColumnProvider, col.HeaderText, !disabledSet.Contains(id)));
                }
            }
            foreach (var prov in manager.AllTranslationProviders.Where(p => p.GetType().Assembly == assembly))
            {
                string id = MakeId(dllName, PluginComponentType.TranslationProvider, prov.GetType().Name);
                string displayName = prov.SupportedCultures.Count > 0
                    ? string.Join(", ", prov.SupportedCultures.Select(LanguageOption.GetLanguageDisplayName))
                    : prov.Name;
                components.Add(new PluginComponentViewModel(id, PluginComponentType.TranslationProvider, displayName, true));
            }
            foreach (var prov in manager.AllThemeProviders.Where(p => p.GetType().Assembly == assembly))
            {
                string id = MakeId(dllName, PluginComponentType.ThemeProvider, prov.GetType().Name);
                var themes = prov.GetThemes().ToList();
                string displayName = themes.Count > 0
                    ? string.Join(", ", themes.Select(t => t.DisplayName))
                    : prov.Name;
                components.Add(new PluginComponentViewModel(id, PluginComponentType.ThemeProvider, displayName, true));
            }
        }

        private static string ResolveDllName(IActionPlugin plugin, string pluginsDir)
        {
            var assembly = plugin.GetType().Assembly;
            string location = assembly.Location;
            return Path.GetFileName(location);
        }

        private static List<PluginComponentViewModel> BuildComponents(
            IActionPlugin plugin,
            string dllName,
            PluginManager manager,
            HashSet<string> disabledSet)
        {
            var components = new List<PluginComponentViewModel>();
            var assembly = plugin.GetType().Assembly;

            foreach (var reg in manager.AllActions.Where(r => r.Plugin == plugin))
            {
                string id = MakeId(dllName, PluginComponentType.Action, reg.Action.Id);
                components.Add(new PluginComponentViewModel(id, PluginComponentType.Action, reg.Action.DisplayName, !disabledSet.Contains(id)));
            }

            AddAssemblyProviders(components, assembly, dllName, manager, disabledSet);
            return components;
        }

        private static string MakeId(string dllName, PluginComponentType type, string name)
            => $"{dllName}::{type}::{name}";
    }
}
