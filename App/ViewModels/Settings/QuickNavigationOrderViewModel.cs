using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.App.Services.PluginManagerCore;
using SwiftList.App.ViewModels.Settings.Plugins;
using SwiftList.Core;

using SwiftList.App.Services.Plugin;
namespace SwiftList.App.ViewModels.Settings;

// Lets the user reorder which IQuickNavigationProvider's entries appear first/last in the quick
// navigation menu's root level -- a global preference, not per-plugin config, so it lives here rather
// than in any plugin's own PluginConfigSchema. Edits stage in Items and only commit to
// _userSettings.QuickNavigationProviderOrder when Save() runs (called from GeneralSettingsViewModel.Apply()).
public class QuickNavigationOrderViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public QuickNavigationOrderViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        // PluginManager.Instance.QuickNavigationProviders already applies both the enabled-components
        // filter and the persisted order (falling back to discovery order for anything unlisted), so
        // this list starts out showing exactly what the menu itself would show, in the same order --
        // disabled providers never appear here, per the "hiding them would be meaningless" call.
        foreach (var provider in PluginManager.Instance.QuickNavigationProviders)
        {
            Items.Add(new QuickNavProviderOrderItem
            {
                Id = BuildId(provider),
                DisplayName = provider.GroupName
            });
        }

        MoveUpCommand = new RelayCommand<QuickNavProviderOrderItem>(MoveUp);
        MoveDownCommand = new RelayCommand<QuickNavProviderOrderItem>(MoveDown);
    }

    public ObservableCollection<QuickNavProviderOrderItem> Items { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    private static string BuildId(PluginSdk.Abstractions.Plugins.IQuickNavigationProvider provider) =>
        PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(provider), PluginComponentType.QuickNavigationProvider, provider.GetType().Name);

    private void MoveUp(QuickNavProviderOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0) Items.Move(idx, idx - 1);
    }

    private void MoveDown(QuickNavProviderOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1) Items.Move(idx, idx + 1);
    }

    public void Save()
    {
        _userSettings.QuickNavigationProviderOrder = Items.Select(x => x.Id).ToList();
    }
}

public class QuickNavProviderOrderItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
