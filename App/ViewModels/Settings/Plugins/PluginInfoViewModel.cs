using System.Collections.ObjectModel;
using System.Collections.Generic;
using SwiftList.App.Services;
using SwiftList.PluginSdk;
using SwiftList.App.ViewModels;

namespace SwiftList.App.ViewModels.Settings.Plugins
{
    /// <summary>
    /// Represents the strongly-typed categories of plugin components.
    /// </summary>
    public enum PluginComponentType
    {
        Action,
        DynamicProvider,
        InstantProvider,
        FilterProvider,
        ColumnProvider,
        AliasProvider,
        ActivePathCollector,
        FileDialogAdapter,
        InlineSearchAdapter,
        /// <summary>Translation providers are displayed read-only; they cannot be disabled.</summary>
        TranslationProvider,
        /// <summary>Theme providers are displayed read-only; they cannot be disabled.</summary>
        ThemeProvider
    }

    /// <summary>
    /// Represents a group of plugin components of the same type.
    /// </summary>
    public class PluginComponentGroupViewModel : ViewModelBase
    {
        public PluginComponentGroupViewModel(PluginComponentType componentType, System.Collections.Generic.List<PluginComponentViewModel> components)
        {
            ComponentType = componentType;
            Components = new ObservableCollection<PluginComponentViewModel>(components);
        }

        public PluginComponentType ComponentType { get; }
        public string GroupName => TranslationManager.Instance[$"Plugins_Type{ComponentType}"];
        public ObservableCollection<PluginComponentViewModel> Components { get; }
    }

    /// <summary>
    /// Represents a loaded plugin with its name, version, source DLL, and grouped sub-components.
    /// </summary>
    public class PluginInfoViewModel : ViewModelBase
    {
        private bool _isExpanded = true;

        public PluginInfoViewModel(
            string name, 
            string version, 
            string dllFileName, 
            string sdkVersion,
            System.Collections.Generic.List<PluginComponentViewModel> components)
        {
            Name = name;
            Version = version;
            DllFileName = dllFileName;
            SdkVersion = sdkVersion;
            RawComponents = components;

            // Group components by type
            var groups = components
                .GroupBy(c => c.ComponentType)
                .OrderBy(g => g.Key)
                .Select(g => new PluginComponentGroupViewModel(g.Key, g.ToList()))
                .ToList();

            ComponentGroups = new ObservableCollection<PluginComponentGroupViewModel>(groups);
        }

        public string Name { get; }
        public string Version { get; }
        public string DllFileName { get; }
        public string SdkVersion { get; }
        public List<PluginComponentViewModel> RawComponents { get; }
        public ObservableCollection<PluginComponentGroupViewModel> ComponentGroups { get; }

        public bool HasNoComponents => RawComponents.Count == 0;

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
    }

    /// <summary>
    /// Represents a single sub-component of a plugin (action, provider, etc.) that can be enabled/disabled.
    /// </summary>
    public class PluginComponentViewModel : ViewModelBase
    {
        private bool _isEnabled;

        public PluginComponentViewModel(string componentId, PluginComponentType componentType, string displayName, bool isEnabled)
        {
            ComponentId = componentId;
            ComponentType = componentType;
            DisplayName = displayName;
            _isEnabled = isEnabled;
        }

        /// <summary>The stable unique ID used to persist the disabled state.</summary>
        public string ComponentId { get; }

        /// <summary>The category/type of this component (strongly-typed enum).</summary>
        public PluginComponentType ComponentType { get; }

        public string DisplayName { get; }

        /// <summary>
        /// Whether the user can toggle this component on/off.
        /// TranslationProvider and ThemeProvider components are shown read-only and cannot be disabled.
        /// </summary>
        public bool IsToggleable => ComponentType != PluginComponentType.TranslationProvider && ComponentType != PluginComponentType.ThemeProvider;

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }
    }
}
