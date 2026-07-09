using SwiftList.Core;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Settings;

internal static class NetworkDriveViewModelHelper
{
    // Split out of NetworkDriveSettingsViewModel to keep that file under the line-count limit.
    // Each of these three category-scoped rebuilds acts only on rows that are already AppliedEnabled (i.e.
    // actually saved in UserSettings already) -- never on whatever happens to be checked live in the UI.
    // These used to persist all three categories' live checkbox state before scanning, which meant clicking
    // "Rebuild" silently applied (and started indexing) a drive/distro/folder someone had just added or
    // re-checked but never confirmed via the window's own Apply/OK.
    public static void RebuildDrives(NetworkDriveSettingsViewModel vm, SearchService searchService, Action? onTriggerFastRefresh)
    {
        if (!vm.CanRebuildDrives) return;

        vm.CanRebuildDrives = false;
        vm.NetworkIndexSummary = TranslationManager.Instance["Network_Rebuilding"];
        searchService.ConfigureNetworkIndexes();
        foreach (var drive in vm.NetworkDrives.Where(d => d.AppliedEnabled))
        {
            searchService.RefreshNetworkDriveIndex(drive.Drive);
        }
        onTriggerFastRefresh?.Invoke();
    }

    public static void RebuildWsl(NetworkDriveSettingsViewModel vm, SearchService searchService, Action? onTriggerFastRefresh)
    {
        if (!vm.CanRebuildWsl) return;

        vm.CanRebuildWsl = false;
        vm.WslIndexSummary = TranslationManager.Instance["Network_Rebuilding"];
        searchService.ConfigureNetworkIndexes();
        foreach (var wsl in vm.WslDrives.Where(w => w.AppliedEnabled))
        {
            searchService.RefreshNetworkDriveIndex(wsl.UncPath);
        }
        onTriggerFastRefresh?.Invoke();
    }

    public static void RebuildFolders(NetworkDriveSettingsViewModel vm, SearchService searchService, Action? onTriggerFastRefresh)
    {
        if (!vm.CanRebuildFolders) return;

        vm.CanRebuildFolders = false;
        vm.FolderIndexSummary = TranslationManager.Instance["Network_Rebuilding"];
        searchService.ConfigureNetworkIndexes();
        foreach (var folder in vm.FolderIndexes.Where(f => f.AppliedEnabled))
        {
            searchService.RefreshNetworkDriveIndex(folder.Path);
        }
        onTriggerFastRefresh?.Invoke();
    }

    public static void RunDriveAction(
        NetworkDriveSettingsItem item,
        NetworkDriveSettingsViewModel vm,
        SearchService searchService,
        Action onTriggerFastRefresh,
        HashSet<string> pendingRowRebuilds,
        HashSet<string> observedRowRebuilds)
    {
        if (!item.CanRunRowAction)
            return;

        item.CanRunRowAction = false;
        if (item.RowAction == NetworkDriveRowAction.Rebuild)
        {
            // RowAction == Rebuild only ever shows for a row that's already AppliedEnabled (see
            // UpdateRowAction), so it's already correctly saved -- no need to re-persist anything here.
            item.State = TranslationManager.Instance["Network_StatusIndexing"];
            item.ItemCount = "-";
            vm.NetworkIndexSummary = TranslationManager.Instance["Network_Rebuilding"];
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
            item.CanEditEnabled = item.IsPresent;
            item.CanEditRefreshMode = item.IsPresent;
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
        SearchService searchService,
        Action onTriggerFastRefresh,
        HashSet<string> pendingRowRebuilds,
        HashSet<string> observedRowRebuilds)
    {
        if (!item.CanRunRowAction)
            return;

        item.CanRunRowAction = false;
        if (item.RowAction == NetworkDriveRowAction.Rebuild)
        {
            // RowAction == Rebuild only ever shows for a row that's already AppliedEnabled (see
            // UpdateWslRowAction), so it's already correctly saved -- no need to re-persist anything here.
            item.State = TranslationManager.Instance["Network_StatusIndexing"];
            item.ItemCount = "-";
            vm.WslIndexSummary = TranslationManager.Instance["Network_Rebuilding"];
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
            item.CanEditEnabled = item.IsPresent;
            item.CanEditRefreshMode = item.IsPresent;
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
        SearchService searchService,
        Action onTriggerFastRefresh,
        HashSet<string> pendingRowRebuilds,
        HashSet<string> observedRowRebuilds)
    {
        if (!item.CanRunRowAction)
            return;

        item.CanRunRowAction = false;
        if (item.RowAction == NetworkDriveRowAction.Rebuild)
        {
            // RowAction == Rebuild only ever shows for a row that's already AppliedEnabled (see
            // UpdateFolderRowAction), so it's already correctly saved -- no need to re-persist anything here.
            item.State = TranslationManager.Instance["Network_StatusIndexing"];
            item.ItemCount = "-";
            vm.FolderIndexSummary = TranslationManager.Instance["Network_Rebuilding"];
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
