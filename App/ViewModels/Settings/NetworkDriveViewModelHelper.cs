using SwiftList.Core;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Settings;

internal static class NetworkDriveViewModelHelper
{
    // Split out of NetworkDriveSettingsViewModel to keep that file under the line-count limit.
    public static void Rebuild(NetworkDriveSettingsViewModel vm, UserSettings userSettings, SearchService searchService, Action? onTriggerFastRefresh)
    {
        if (!vm.CanRebuild) return;
        vm.IsBusy = true;

        userSettings.NetworkDrives = vm.NetworkDrives.Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Id)).Select(d => new NetworkDriveSetting
        {
            Id = d.Id,
            RefreshMode = d.RefreshMode
        }).ToList();
        userSettings.WslSettings = vm.WslDrives.Where(w => w.IsEnabled && !string.IsNullOrWhiteSpace(w.Id)).Select(w => new WslSetting
        {
            Id = w.Id,
            RefreshMode = w.RefreshMode
        }).ToList();
        userSettings.Save();
        vm.ResetPendingEdits();

        vm.CanRebuild = false;
        vm.IndexSummary = TranslationManager.Instance["Network_Rebuilding"];
        searchService.RefreshNetworkIndexes();
        onTriggerFastRefresh?.Invoke();
    }

    public static void RunDriveAction(
        NetworkDriveSettingsItem item,
        NetworkDriveSettingsViewModel vm,
        UserSettings userSettings,
        SearchService searchService,
        Action onTriggerFastRefresh,
        HashSet<string> pendingRowRebuilds,
        HashSet<string> observedRowRebuilds)
    {
        if (vm.IsBusy || !item.CanRunRowAction)
            return;

        item.CanRunRowAction = false;
        if (item.RowAction == NetworkDriveRowAction.Rebuild)
        {
            vm.IsBusy = true;
            userSettings.NetworkDrives = vm.NetworkDrives
                .Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Id))
                .Select(d => new NetworkDriveSetting { Id = d.Id, RefreshMode = d.RefreshMode })
                .ToList();
            userSettings.WslSettings = vm.WslDrives
                .Where(w => w.IsEnabled && !string.IsNullOrWhiteSpace(w.Id))
                .Select(w => new WslSetting { Id = w.Id, RefreshMode = w.RefreshMode })
                .ToList();
            userSettings.Save();
            searchService.ConfigureNetworkIndexes();
            vm.ResetPendingEdits();
            item.State = TranslationManager.Instance["Network_StatusIndexing"];
            item.ItemCount = "-";
            vm.IndexSummary = TranslationManager.Instance["Network_Rebuilding"];
            pendingRowRebuilds.Add(item.Drive);
            if (!searchService.RefreshNetworkDriveIndex(item.Drive))
            {
                pendingRowRebuilds.Remove(item.Drive);
                observedRowRebuilds.Remove(item.Drive);
            }
        }
        else if (item.RowAction == NetworkDriveRowAction.Delete)
        {
            searchService.DeleteNetworkDriveCache(item.Drive);
            item.RowAction = NetworkDriveRowAction.None;
            item.State = item.IsPresent ? TranslationManager.Instance["Network_StatusConnected"] : TranslationManager.Instance["Network_StatusUnavailable"];
            item.ItemCount = "-";
            item.CanRunRowAction = false;
            item.CanEditEnabled = item.IsPresent && !vm.IsBusy;
            item.CanEditRefreshMode = item.IsPresent && !vm.IsBusy;
            if (!item.IsPresent)
                item.IsEnabled = false;
        }
        onTriggerFastRefresh?.Invoke();
    }

    public static void RunWslDriveAction(
        WslSettingsItem item,
        NetworkDriveSettingsViewModel vm,
        UserSettings userSettings,
        SearchService searchService,
        Action onTriggerFastRefresh,
        HashSet<string> pendingRowRebuilds,
        HashSet<string> observedRowRebuilds)
    {
        if (vm.IsBusy || !item.CanRunRowAction)
            return;

        item.CanRunRowAction = false;
        if (item.RowAction == NetworkDriveRowAction.Rebuild)
        {
            vm.IsBusy = true;
            userSettings.NetworkDrives = vm.NetworkDrives
                .Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Id))
                .Select(d => new NetworkDriveSetting { Id = d.Id, RefreshMode = d.RefreshMode })
                .ToList();
            userSettings.WslSettings = vm.WslDrives
                .Where(w => w.IsEnabled && !string.IsNullOrWhiteSpace(w.Id))
                .Select(w => new WslSetting { Id = w.Id, RefreshMode = w.RefreshMode })
                .ToList();
            userSettings.Save();
            searchService.ConfigureNetworkIndexes();
            vm.ResetPendingEdits();
            item.State = TranslationManager.Instance["Network_StatusIndexing"];
            item.ItemCount = "-";
            vm.IndexSummary = TranslationManager.Instance["Network_Rebuilding"];
            pendingRowRebuilds.Add(item.UncPath);
            if (!searchService.RefreshNetworkDriveIndex(item.UncPath))
            {
                pendingRowRebuilds.Remove(item.UncPath);
                observedRowRebuilds.Remove(item.UncPath);
            }
        }
        else if (item.RowAction == NetworkDriveRowAction.Delete)
        {
            searchService.DeleteNetworkDriveCache(item.UncPath);
            item.RowAction = NetworkDriveRowAction.None;
            item.State = item.IsPresent ? TranslationManager.Instance["Network_StatusConnected"] : TranslationManager.Instance["Network_StatusUnavailable"];
            item.ItemCount = "-";
            item.CanRunRowAction = false;
            item.CanEditEnabled = item.IsPresent && !vm.IsBusy;
            item.CanEditRefreshMode = item.IsPresent && !vm.IsBusy;
            if (!item.IsPresent)
                item.IsEnabled = false;
        }
        onTriggerFastRefresh?.Invoke();
    }
}
