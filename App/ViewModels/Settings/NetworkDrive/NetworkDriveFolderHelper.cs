using System.IO;
using SwiftList.Core;
using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.App.Views.Controls;

namespace SwiftList.App.ViewModels.Settings.NetworkDrive;

// Folder-index half of NetworkDriveSettingsViewModel -- a third row category alongside NetworkDrives/
// WslDrives, riding the same NetworkIndexer/Scheduler machinery with a full folder path as the opaque
// key instead of a drive letter or WSL UNC path. Extracted (composition, not a partial class) to keep
// the main file under the line limit; also owns the two big per-refresh methods (UpdateRowsInPlace/
// RebuildRows) since they now interleave all three categories.
internal static class NetworkDriveFolderHelper
{
    public static void AddFolder(NetworkDriveSettingsViewModel vm, SearchService searchService, Action onTriggerFastRefresh, HashSet<string> pendingRowRebuilds, HashSet<string> observedRowRebuilds)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            return;

        // A local drive root or WSL distro root belongs on the "网络驱动器"/"本地驱动器"/WSL tabs
        // (whole-volume indexing), not here. A UNC share root ("\\server\share") is let through, though
        // -- unlike a local drive, there's no drive-letter tab that can index an unmapped share at all,
        // so the share root is the finest-grained indexable unit available for it.
        if (IsDriveRoot(dialog.FolderName))
        {
            CustomMessageBox.Show(
                TranslationManager.Instance["Folder_RootNotAllowed"],
                TranslationManager.Instance["Executor_PromptTitle"],
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var path = dialog.FolderName.TrimEnd('\\');
        if (vm.FolderIndexes.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;

        var item = new FolderIndexSettingsItem { Path = path, IsEnabled = true, IsPresent = true };
        item.RowActionCommand = new RelayCommand(
            () => NetworkDriveViewModelHelper.RunFolderIndexAction(item, vm, searchService, onTriggerFastRefresh, pendingRowRebuilds, observedRowRebuilds),
            () => item.CanRunRowAction);
        item.PropertyChanged += vm.OnFolderItemChanged;
        vm.FolderIndexes.Add(item);
        vm.HasPendingEdits = true;
        vm.NotifyFolderIndexesEmptyChanged();
    }

    private static bool IsDriveRoot(string path)
    {
        try
        {
            var trimmed = path.TrimEnd('\\');
            // A non-WSL UNC path ("\\server\share", including its own root) has no drive-letter tab
            // that can index it, so it's never blocked here regardless of depth -- unlike a local
            // drive, even the share root itself is a legitimate folder-index target. A WSL path falls
            // through to the same root-vs-subfolder comparison below as a local drive: only the exact
            // distro root ("\\wsl$\Ubuntu") stays blocked (it already has its own tab), a subfolder
            // within it ("\\wsl$\Ubuntu\home\user\projects") is just as legitimate a target as a UNC
            // share subfolder.
            if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) && !NetworkDriveSettingsHelper.IsWslPath(trimmed))
                return false;

            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && trimmed.Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // Configured (user-added, from UserSettings) union'd with anything the cache still remembers -- so a
    // folder whose entry got unchecked but not deleted still shows a Delete row, mirroring visibleWsl. Also
    // union'd with whatever's live in FolderIndexes itself -- a row AddFolder just created has neither a
    // UserSettings entry (nothing's been Applied yet) nor a cache (never scanned), so without this it would
    // never appear in this list at all and UpdateRowsInPlace would silently never touch it again: no state
    // text, no item count, no row action, forever, until Apply -- and no way to back out of the addition
    // without going through Apply first.
    public static List<string> GetVisibleFolders(NetworkDriveSettingsViewModel vm, SearchService searchService, UserSettings userSettings)
    {
        var configuredPaths = userSettings.FolderIndexes
            .Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .Select(f => f.Path.TrimEnd('\\'));
        // A folder-index key is longer than a bare drive letter and isn't a WSL distro's "\\wsl$\..." key
        // -- a genuine UNC share key ("\\server\share", now indexable via the folder-index feature) is
        // NOT excluded here, only WSL is.
        var cachedPaths = searchService.GetCachedNetworkDrives()
            .Where(d => d.Length > 1 && !NetworkDriveSettingsHelper.IsWslPath(d));
        var livePaths = vm.FolderIndexes.Select(f => f.Path);
        return configuredPaths.Concat(cachedPaths).Concat(livePaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void UpdateRowsInPlace(
        NetworkDriveSettingsViewModel vm, SearchService searchService,
        List<string> visibleDrives, List<string> visibleWsl, List<string> visibleFolders,
        Dictionary<string, NetworkIndexStatus> statuses, Dictionary<string, ResolvedNetworkDrive> resolvedByDrive, List<string> wslDistros,
        Dictionary<string, NetworkDriveSetting> configured, Dictionary<string, WslSetting> configuredWsl, Dictionary<string, FolderIndexSetting> configuredFolders)
    {
        foreach (var letter in visibleDrives)
        {
            var item = vm.NetworkDrives.FirstOrDefault(d => d.Drive.Equals(letter, StringComparison.OrdinalIgnoreCase));
            if (item == null) continue;
            statuses.TryGetValue(letter, out var indexStatus);
            resolvedByDrive.TryGetValue(letter, out var drive);
            item.Id = NetworkDriveResolver.GetNetworkId(letter);
            item.IsPresent = drive != null;
            if (!item.IsPresent) item.IsEnabled = false;
            vm.TrackPendingRebuild(letter, indexStatus?.State);
            item.State = drive == null ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(drive, indexStatus);
            item.ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-";
            configured.TryGetValue(item.Id, out var saved);
            vm.UpdateRowAction(item, item.IsPresent && saved != null, indexStatus?.State, i => searchService.HasNetworkDriveCache(i.Drive));
        }
        foreach (var name in visibleWsl)
        {
            var item = vm.WslDrives.FirstOrDefault(d => d.DistroName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (item == null) continue;
            var unc = $@"\\wsl$\{name}";
            statuses.TryGetValue(unc, out var indexStatus);
            var isPresent = wslDistros.Contains(name, StringComparer.OrdinalIgnoreCase);
            item.IsPresent = isPresent;
            if (!item.IsPresent) item.IsEnabled = false;
            vm.TrackPendingRebuild(unc, indexStatus?.State);
            item.State = !isPresent ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(null, indexStatus);
            item.ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-";
            configuredWsl.TryGetValue(name, out var saved);
            vm.UpdateRowAction(item, isPresent && saved != null, indexStatus?.State, i => searchService.HasNetworkDriveCache(i.UncPath));
        }
        foreach (var path in visibleFolders)
        {
            var item = vm.FolderIndexes.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (item == null) continue;
            statuses.TryGetValue(path, out var indexStatus);
            var isPresent = Directory.Exists(path);
            item.IsPresent = isPresent;
            if (!isPresent) item.IsEnabled = false;
            vm.TrackPendingRebuild(path, indexStatus?.State);
            item.State = !isPresent ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(null, indexStatus);
            item.ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-";
            configuredFolders.TryGetValue(path, out var saved);
            // Unlike a drive/WSL row (which stays visible as "an OS-resolvable thing you're just not
            // indexing" even after Delete), a folder row only exists because the user explicitly added
            // it -- there's no other reason to keep showing it once unchecked, whether or not it ever
            // got far enough to have a cache. So Delete is unconditional here, not gated on cache state.
            vm.UpdateRowAction(item, isPresent && saved != null, indexStatus?.State, _ => true);
        }
    }

    public static void RebuildRows(
        NetworkDriveSettingsViewModel vm, SearchService searchService, Action onTriggerFastRefresh, HashSet<string> pendingRowRebuilds, HashSet<string> observedRowRebuilds,
        List<string> visibleDrives, List<string> visibleWsl, List<string> visibleFolders,
        Dictionary<string, NetworkIndexStatus> statuses, Dictionary<string, ResolvedNetworkDrive> resolvedByDrive, List<string> wslDistros,
        Dictionary<string, NetworkDriveSetting> configured, Dictionary<string, WslSetting> configuredWsl, Dictionary<string, FolderIndexSetting> configuredFolders)
    {
        foreach (var existing in vm.NetworkDrives) existing.PropertyChanged -= vm.OnNetworkDriveItemChanged;
        vm.NetworkDrives.Clear();
        foreach (var letter in visibleDrives)
        {
            statuses.TryGetValue(letter, out var indexStatus);
            resolvedByDrive.TryGetValue(letter, out var drive);
            var id = NetworkDriveResolver.GetNetworkId(letter);
            configured.TryGetValue(id, out var saved);
            var item = new NetworkDriveSettingsItem
            {
                Id = id,
                Drive = letter,
                IsPresent = drive != null,
                IsEnabled = drive != null && saved != null,
                State = drive == null ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(drive, indexStatus),
                ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-",
                RefreshMode = NetworkDriveSettingsHelper.NormalizeRefreshMode(saved?.RefreshMode)
            };
            item.RowActionCommand = new RelayCommand(() => NetworkDriveViewModelHelper.RunDriveAction(item, vm, searchService, onTriggerFastRefresh, pendingRowRebuilds, observedRowRebuilds), () => item.CanRunRowAction);
            vm.TrackPendingRebuild(letter, indexStatus?.State);
            vm.UpdateRowAction(item, drive != null && saved != null, indexStatus?.State, i => searchService.HasNetworkDriveCache(i.Drive));
            item.PropertyChanged += vm.OnNetworkDriveItemChanged;
            vm.NetworkDrives.Add(item);
        }

        foreach (var existing in vm.WslDrives) existing.PropertyChanged -= vm.OnWslDriveItemChanged;
        vm.WslDrives.Clear();
        foreach (var name in visibleWsl)
        {
            var unc = $@"\\wsl$\{name}";
            statuses.TryGetValue(unc, out var indexStatus);
            var isPresent = wslDistros.Contains(name, StringComparer.OrdinalIgnoreCase);
            configuredWsl.TryGetValue(name, out var saved);
            var item = new WslSettingsItem
            {
                Id = name,
                DistroName = name,
                IsPresent = isPresent,
                IsEnabled = isPresent && saved != null,
                State = !isPresent ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(null, indexStatus),
                ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-",
                RefreshMode = NetworkDriveSettingsHelper.NormalizeRefreshMode(saved?.RefreshMode)
            };
            item.RowActionCommand = new RelayCommand(() => NetworkDriveViewModelHelper.RunWslDriveAction(item, vm, searchService, onTriggerFastRefresh, pendingRowRebuilds, observedRowRebuilds), () => item.CanRunRowAction);
            vm.TrackPendingRebuild(unc, indexStatus?.State);
            vm.UpdateRowAction(item, isPresent && saved != null, indexStatus?.State, i => searchService.HasNetworkDriveCache(i.UncPath));
            item.PropertyChanged += vm.OnWslDriveItemChanged;
            vm.WslDrives.Add(item);
        }

        foreach (var existing in vm.FolderIndexes) existing.PropertyChanged -= vm.OnFolderItemChanged;
        vm.FolderIndexes.Clear();
        foreach (var path in visibleFolders)
        {
            statuses.TryGetValue(path, out var indexStatus);
            var isPresent = Directory.Exists(path);
            configuredFolders.TryGetValue(path, out var saved);
            var item = new FolderIndexSettingsItem
            {
                Path = path,
                IsPresent = isPresent,
                IsEnabled = isPresent && saved != null,
                State = !isPresent ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(null, indexStatus),
                ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-",
                RefreshMode = NetworkDriveSettingsHelper.NormalizeRefreshMode(saved?.RefreshMode)
            };
            item.RowActionCommand = new RelayCommand(() => NetworkDriveViewModelHelper.RunFolderIndexAction(item, vm, searchService, onTriggerFastRefresh, pendingRowRebuilds, observedRowRebuilds), () => item.CanRunRowAction);
            vm.TrackPendingRebuild(path, indexStatus?.State);
            vm.UpdateRowAction(item, isPresent && saved != null, indexStatus?.State, _ => true);
            item.PropertyChanged += vm.OnFolderItemChanged;
            vm.FolderIndexes.Add(item);
        }
    }
}
