using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;

using SwiftList.App.Services.Plugin;
using SwiftList.App.ViewModels.Search.StartupPanel;
namespace SwiftList.App.ViewModels.Settings.StartupPanel;

// Lets the user reorder the Startup Panel's tab strip -- both built-in tabs (Recent Files, Last
// Directory) and plugin-provided ones, all in one flat list (unlike PluginTabGroups above, which is
// grouped per-plugin for the enable/disable checkboxes and has no cross-plugin ordering concept).
// Edits stage in Items and only commit to _userSettings.StartupPanel.TabOrder when Save() runs (called
// from StartupPanelSettingsViewModel.Save(), itself called from SettingsViewModel.Apply()).
public class StartupPanelTabOrderViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public StartupPanelTabOrderViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        Refresh(userSettings.StartupPanel.RecentFilesEnabled, userSettings.StartupPanel.LastDirectoryEnabled);

        MoveUpCommand = new RelayCommand<StartupPanelTabOrderItem>(MoveUp);
        MoveDownCommand = new RelayCommand<StartupPanelTabOrderItem>(MoveDown);
    }

    public ObservableCollection<StartupPanelTabOrderItem> Items { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    // Called on construction, and again whenever the user switches to this sub-tab (see
    // StartupPanelSettingsViewModel.SelectedSubTab), so a Recent Files/Last Directory checkbox flipped
    // earlier in the same Settings session -- staged locally in that ViewModel, not committed to
    // UserSettings until Save() -- is reflected here too. recentFilesEnabled/lastDirectoryEnabled are
    // passed in for exactly that reason rather than read from _userSettings directly; ClosedTabIds
    // (plugin tabs) doesn't need the same treatment since StartupPanelPluginTabViewModel.IsOpen already
    // writes it straight through to the live UserSettings object on every toggle. Otherwise mirrors
    // StartupPanelController.BuildCandidateSources' own candidate set exactly, just without fetching
    // any actual items.
    public void Refresh(bool recentFilesEnabled, bool lastDirectoryEnabled)
    {
        Items.Clear();

        var panel = _userSettings.StartupPanel;
        var candidates = new List<StartupPanelTabOrderItem>();

        if (recentFilesEnabled)
            candidates.Add(new StartupPanelTabOrderItem
            {
                Id = RecentFilesTabSource.SourceId,
                DisplayName = TranslationManager.Instance["StartupPanel_TabRecentFiles"]
            });
        if (lastDirectoryEnabled)
            candidates.Add(new StartupPanelTabOrderItem
            {
                Id = LastDirectoryTabSource.SourceId,
                DisplayName = TranslationManager.Instance["StartupPanel_TabLastDirectory"]
            });

        var closedIds = panel.ClosedTabIds;
        foreach (var provider in PluginManager.Instance.StartupPanelTabProviders)
        {
            var id = PluginTabSource.ComponentId(provider);
            if (!closedIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                candidates.Add(new StartupPanelTabOrderItem { Id = id, DisplayName = provider.Name });
        }

        var order = panel.TabOrder;
        foreach (var item in candidates.OrderBy(c =>
                 {
                     var rank = order.IndexOf(c.Id);
                     return rank >= 0 ? rank : int.MaxValue;
                 }))
        {
            Items.Add(item);
        }
    }

    private void MoveUp(StartupPanelTabOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0) Items.Move(idx, idx - 1);
    }

    private void MoveDown(StartupPanelTabOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1) Items.Move(idx, idx + 1);
    }

    public void Save() => _userSettings.StartupPanel.TabOrder = Items.Select(x => x.Id).ToList();
}

public class StartupPanelTabOrderItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
