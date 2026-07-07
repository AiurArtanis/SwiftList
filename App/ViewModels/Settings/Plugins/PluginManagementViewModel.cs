using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;
using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.App.ViewModels.Settings.Plugins;

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
        Plugins = new ObservableCollection<PluginInfoViewModel>(PluginLoaderHelper.BuildPluginList(_userSettings));
        ToggleExpandCommand = new RelayCommand<PluginInfoViewModel>(p => p?.IsExpanded = !p.IsExpanded);
        ConfigurePluginCommand = new RelayCommand<PluginInfoViewModel>(p =>
        {
            if (p == null) return;
            var window = new Views.Settings.Plugins.PluginConfigWindow(p);
            var activeWindow = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
                               ?? System.Windows.Application.Current.MainWindow;
            if (activeWindow != null && activeWindow != window)
            {
                window.Owner = activeWindow;
            }
            window.ShowDialog();
            if (!window.IsSaved)
            {
                foreach (var field in p.ConfigFields)
                {
                    field.Reload();
                }
            }
        });

        // Dynamically refresh the plugin list when language changes to dynamically apply localized plugin names
        TranslationManager.Instance.PropertyChanged += (s, e) =>
        {
            var expandedStates = Plugins.ToDictionary(p => p.DllFileName, p => p.IsExpanded);
            var newList = PluginLoaderHelper.BuildPluginList(_userSettings);
            Plugins.Clear();
            foreach (var p in newList)
            {
                if (expandedStates.TryGetValue(p.DllFileName, out var isExpanded))
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
    public ICommand ConfigurePluginCommand { get; }

    public bool IsEmpty => Plugins.Count == 0;

    public string HostSdkVersion { get; } = typeof(IPlugin).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public void Save()
    {
        // Apply only the components the user actually toggled on this page (IsDirty), merged into the
        // CURRENT disabled list rather than replacing it wholesale -- Plugins is a snapshot taken when
        // the Settings window opened, so a blind replace would silently revert any component disabled
        // or re-enabled through another channel since then (e.g. a Startup Panel tab's x button, or the
        // Startup Panel settings page's own re-enable checkbox).
        var disabled = new HashSet<string>(_userSettings.DisabledPluginComponents, StringComparer.OrdinalIgnoreCase);

        foreach (var c in Plugins.SelectMany(p => p.RawComponents).Where(c => c.IsToggleable && c.IsDirty))
        {
            if (c.IsEnabled)
                disabled.Remove(c.ComponentId);
            else
                disabled.Add(c.ComponentId);
        }

        _userSettings.DisabledPluginComponents = disabled.ToList();
    }
}
