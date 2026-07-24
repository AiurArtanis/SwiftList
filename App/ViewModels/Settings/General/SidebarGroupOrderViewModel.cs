using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services.PluginManagerCore;
using SwiftList.App.ViewModels.Settings.Plugins;
using SwiftList.Core;
using SwiftList.PluginSdk.Abstractions.Plugins;

using SwiftList.App.Services.Plugin;
namespace SwiftList.App.ViewModels.Settings.General;

// Lets the user reorder the full SearchWindow's sidebar filter groups (Type/Date/Size/any third-party
// ISidebarFilterProvider) -- one entry per PROVIDER rather than per group, matching how
// PluginManager.SidebarFilterProviders already orders at provider granularity. Edits stage in Items and
// only commit to _userSettings.SidebarGroupOrder when Save() runs (called from
// GeneralSettingsViewModel.Apply()).
public class SidebarGroupOrderViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public SidebarGroupOrderViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        // PluginManager.Instance.SidebarFilterProviders already applies both the enabled-components
        // filter (via FilteredSidebarFilterProvider) and the persisted order, so this list starts out
        // showing exactly what the sidebar itself would show, in the same order. A provider whose every
        // group is currently disabled contributes no groups -- skipped here too, since reordering an
        // entry with nothing to show would be meaningless.
        foreach (var provider in PluginManager.Instance.SidebarFilterProviders)
        {
            var groups = provider.GetFilterGroups().ToList();
            if (groups.Count == 0) continue;

            Items.Add(new SidebarGroupOrderItem
            {
                // BuildId must match PluginManager.SidebarFilterProviders' own ordering exactly, which
                // computes its id off the RAW plugin-defined provider -- provider here is always a
                // FilteredSidebarFilterProvider wrapper (see PluginManager.SidebarFilterProviders), so
                // unwrap it first or the id would come from the wrapper's own type/assembly instead
                // and never match anything the user's saved order could reference.
                Id = BuildId(provider is FilteredSidebarFilterProvider filtered ? filtered.Inner : provider),
                DisplayName = groups[0].Header
            });
        }

        MoveUpCommand = new RelayCommand<SidebarGroupOrderItem>(MoveUp);
        MoveDownCommand = new RelayCommand<SidebarGroupOrderItem>(MoveDown);
    }

    public ObservableCollection<SidebarGroupOrderItem> Items { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    internal static string BuildId(ISidebarFilterProvider provider) =>
        PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(provider), PluginComponentType.FilterProvider, provider.GetType().Name);

    private void MoveUp(SidebarGroupOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0) Items.Move(idx, idx - 1);
    }

    private void MoveDown(SidebarGroupOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1) Items.Move(idx, idx + 1);
    }

    public void Save()
    {
        _userSettings.SidebarGroupOrder = Items.Select(x => x.Id).ToList();
    }
}

public class SidebarGroupOrderItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
