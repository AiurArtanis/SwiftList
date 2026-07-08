using System.Collections.ObjectModel;

namespace SwiftList.App.ViewModels.Settings;

// One entry per plugin DLL that contributes at least one Startup Panel tab -- groups the "Plugin Tabs"
// reopen list by owning plugin, mirroring how the Plugin Management page groups components by plugin.
public class StartupPanelPluginGroupViewModel
{
    public StartupPanelPluginGroupViewModel(string pluginName, List<StartupPanelPluginTabViewModel> tabs)
    {
        PluginName = pluginName;
        Tabs = new ObservableCollection<StartupPanelPluginTabViewModel>(tabs);
    }

    public string PluginName { get; }
    public ObservableCollection<StartupPanelPluginTabViewModel> Tabs { get; }
}
