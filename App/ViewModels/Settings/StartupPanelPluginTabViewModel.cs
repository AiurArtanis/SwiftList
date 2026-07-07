using SwiftList.App.ViewModels.Search;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

// Lets the user reopen a plugin-provided Startup Panel tab (History/Favorites/...) that was closed via
// its x button in the live panel. This is a panel-local "shown or hidden" toggle backed by
// StartupPanel.ClosedTabIds -- deliberately separate from the plugin component's own enable/disable in
// the Plugin Management settings page (UserSettings.DisabledPluginComponents), which governs whether the
// provider is loaded/used at all. A component disabled there never reaches this list in the first place
// (see StartupPanelSettingsViewModel, which only enumerates PluginManager.StartupPanelTabProviders, the
// already-filtered collection). Writes through immediately on toggle, matching the x button's own
// immediate effect.
public class StartupPanelPluginTabViewModel : ViewModelBase
{
    private readonly string _componentId;

    public StartupPanelPluginTabViewModel(PluginSdk.Abstractions.Plugins.IStartupPanelTabProvider provider)
    {
        _componentId = PluginTabSource.ComponentId(provider);
        Label = provider.Name;
        _isOpen = !UserSettings.Load().StartupPanel.ClosedTabIds.Contains(_componentId, StringComparer.OrdinalIgnoreCase);
    }

    public string Label { get; }

    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (!SetProperty(ref _isOpen, value))
                return;

            var settings = UserSettings.Load();
            settings.StartupPanel.ClosedTabIds.RemoveAll(x => string.Equals(x, _componentId, StringComparison.OrdinalIgnoreCase));
            if (!value)
                settings.StartupPanel.ClosedTabIds.Add(_componentId);

            settings.Save();
        }
    }
}
