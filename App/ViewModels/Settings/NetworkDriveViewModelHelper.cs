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
        userSettings.FolderIndexes = vm.FolderIndexes.Where(f => f.IsEnabled && !string.IsNullOrWhiteSpace(f.Path)).Select(f => new FolderIndexSetting
        {
            Path = f.Path,
            RefreshMode = f.RefreshMode
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
        // A row showing Stop is exactly what's causing vm.IsBusy -- it must stay clickable through that.
        if ((vm.IsBusy && item.RowAction != NetworkDriveRowAction.Stop) || !item.CanRunRowAction)
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
            userSettings.FolderIndexes = vm.FolderIndexes
                .Where(f => f.IsEnabled && !string.IsNullOrWhiteSpace(f.Path))
                .Select(f => new FolderIndexSetting { Path = f.Path, RefreshMode = f.RefreshMode })
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
        else if (item.RowAction == NetworkDriveRowAction.Stop)
        {
            // Don't touch item.State/RowAction here -- the next status poll (RefreshNetworkDrives) will
            // pick up whatever Scheduler.CancelDrive actually settles on and re-derive both correctly.
            pendingRowRebuilds.Remove(item.Drive);
            observedRowRebuilds.Remove(item.Drive);
            searchService.CancelNetworkDriveIndex(item.Drive);
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
        // A row showing Stop is exactly what's causing vm.IsBusy -- it must stay clickable through that.
        if ((vm.IsBusy && item.RowAction != NetworkDriveRowAction.Stop) || !item.CanRunRowAction)
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
            userSettings.FolderIndexes = vm.FolderIndexes
                .Where(f => f.IsEnabled && !string.IsNullOrWhiteSpace(f.Path))
                .Select(f => new FolderIndexSetting { Path = f.Path, RefreshMode = f.RefreshMode })
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
        else if (item.RowAction == NetworkDriveRowAction.Stop)
        {
            pendingRowRebuilds.Remove(item.UncPath);
            observedRowRebuilds.Remove(item.UncPath);
            searchService.CancelNetworkDriveIndex(item.UncPath);
        }
        onTriggerFastRefresh?.Invoke();
    }

    public static void RunFolderIndexAction(
        FolderIndexSettingsItem item,
        NetworkDriveSettingsViewModel vm,
        UserSettings userSettings,
        SearchService searchService,
        Action onTriggerFastRefresh,
        HashSet<string> pendingRowRebuilds,
        HashSet<string> observedRowRebuilds)
    {
        // A row showing Stop is exactly what's causing vm.IsBusy -- it must stay clickable through that.
        // Delete also stays clickable: vm.IsBusy can be true purely because the unrelated local USN
        // service is unreachable, which has nothing to do with removing a folder row (a local-only,
        // never-applied-or-cached entry has no live indexing state to race with).
        if ((vm.IsBusy && item.RowAction is not (NetworkDriveRowAction.Stop or NetworkDriveRowAction.Delete)) || !item.CanRunRowAction)
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
            userSettings.FolderIndexes = vm.FolderIndexes
                .Where(f => f.IsEnabled && !string.IsNullOrWhiteSpace(f.Path))
                .Select(f => new FolderIndexSetting { Path = f.Path, RefreshMode = f.RefreshMode })
                .ToList();
            userSettings.Save();
            searchService.ConfigureNetworkIndexes();
            vm.ResetPendingEdits();
            item.State = TranslationManager.Instance["Network_StatusIndexing"];
            item.ItemCount = "-";
            vm.IndexSummary = TranslationManager.Instance["Network_Rebuilding"];
            pendingRowRebuilds.Add(item.Path);
            if (!searchService.RefreshNetworkDriveIndex(item.Path))
            {
                pendingRowRebuilds.Remove(item.Path);
                observedRowRebuilds.Remove(item.Path);
            }
        }
        else if (item.RowAction == NetworkDriveRowAction.Delete)
        {
            // Unlike a drive/WSL row, a folder row has no OS-resolvable identity to fall back to once
            // it's deleted -- remove it from the list entirely instead of just clearing its RowAction.
            searchService.DeleteNetworkDriveCache(item.Path);
            pendingRowRebuilds.Remove(item.Path);
            observedRowRebuilds.Remove(item.Path);
            vm.RemoveFolderIndex(item);
        }
        else if (item.RowAction == NetworkDriveRowAction.Stop)
        {
            pendingRowRebuilds.Remove(item.Path);
            observedRowRebuilds.Remove(item.Path);
            searchService.CancelNetworkDriveIndex(item.Path);
        }
        onTriggerFastRefresh?.Invoke();
    }
}
