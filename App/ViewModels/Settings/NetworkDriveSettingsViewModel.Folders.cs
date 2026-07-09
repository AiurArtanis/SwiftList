using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Views.Controls;
using SwiftList.Core;
using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Settings;

// Folder-index half of NetworkDriveSettingsViewModel -- a third row category alongside NetworkDrives/
// WslDrives, riding the same NetworkIndexer/Scheduler machinery with a full folder path as the opaque
// key instead of a drive letter or WSL UNC path. Split out to keep the main file under the line limit;
// also owns the two big per-refresh methods (UpdateRowsInPlace/RebuildRows) since they now interleave
// all three categories.
public partial class NetworkDriveSettingsViewModel
{
    private ICommand? _addFolderCommand;

    public ObservableCollection<FolderIndexSettingsItem> FolderIndexes { get; } = new();
    public bool IsFolderIndexesEmpty => FolderIndexes.Count == 0;

    public ICommand AddFolderCommand => _addFolderCommand ??= new RelayCommand(AddFolder);

    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            return;

        // A drive/share root belongs on the "网络驱动器"/"本地驱动器" tabs (whole-drive indexing), not
        // here -- folder indexing exists specifically for a single subfolder, not an entire volume.
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
        if (FolderIndexes.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;

        var item = new FolderIndexSettingsItem { Path = path, IsEnabled = true, IsPresent = true };
        item.RowActionCommand = new RelayCommand(
            () => NetworkDriveViewModelHelper.RunFolderIndexAction(item, this, _userSettings, _searchService, _onTriggerFastRefresh, _pendingRowRebuilds, _observedRowRebuilds),
            () => item.CanRunRowAction);
        item.PropertyChanged += OnFolderItemChanged;
        FolderIndexes.Add(item);
        HasPendingEdits = true;
        OnPropertyChanged(nameof(IsFolderIndexesEmpty));
    }

    private static bool IsDriveRoot(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && path.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void OnFolderItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FolderIndexSettingsItem.IsEnabled) or nameof(FolderIndexSettingsItem.RefreshMode)) HasPendingEdits = true;
    }

    private void UpdateFolderRowAction(FolderIndexSettingsItem item, bool appliedEnabled, string? state)
    {
        item.AppliedEnabled = appliedEnabled;
        // Unlike a drive/WSL row (which stays visible as "an OS-resolvable thing you're just not
        // indexing" even after Delete), a folder row only exists because the user explicitly added it --
        // there's no other reason to keep showing it once it's unchecked, whether or not it ever got far
        // enough to have a cache. So Delete is unconditional here, not gated on HasNetworkDriveCache.
        item.RowAction = appliedEnabled
            ? (state == "indexing" ? NetworkDriveRowAction.Stop : NetworkDriveRowAction.Rebuild)
            : NetworkDriveRowAction.Delete;
    }

    // Called from NetworkDriveViewModelHelper.RunFolderIndexAction's Delete branch -- removes the row
    // from view entirely (not just resetting its RowAction, since there's nothing left to show for it).
    internal void RemoveFolderIndex(FolderIndexSettingsItem item)
    {
        item.PropertyChanged -= OnFolderItemChanged;
        FolderIndexes.Remove(item);
        OnPropertyChanged(nameof(IsFolderIndexesEmpty));
    }

    // Configured (user-added, from UserSettings) union'd with anything the cache still remembers -- so a
    // folder whose entry got unchecked but not deleted still shows a Delete row, mirroring visibleWsl.
    private List<string> GetVisibleFolders(UserSettings userSettings)
    {
        var configuredPaths = userSettings.FolderIndexes
            .Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .Select(f => f.Path.TrimEnd('\\'));
        // A folder-index key is longer than a bare drive letter and doesn't start with "\\" (that's WSL/
        // UNC) -- the same discriminator NetworkIndexerSearchExtensions.IsDriveAllowed uses.
        var cachedPaths = _searchService.GetCachedNetworkDrives()
            .Where(d => d.Length > 1 && !d.StartsWith(@"\\"));
        return configuredPaths.Concat(cachedPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void UpdateRowsInPlace(
        List<string> visibleDrives, List<string> visibleWsl, List<string> visibleFolders,
        Dictionary<string, NetworkIndexStatus> statuses, Dictionary<string, ResolvedNetworkDrive> resolvedByDrive, List<string> wslDistros,
        Dictionary<string, NetworkDriveSetting> configured, Dictionary<string, WslSetting> configuredWsl, Dictionary<string, FolderIndexSetting> configuredFolders)
    {
        foreach (var letter in visibleDrives)
        {
            var item = NetworkDrives.FirstOrDefault(d => d.Drive.Equals(letter, StringComparison.OrdinalIgnoreCase));
            if (item == null) continue;
            statuses.TryGetValue(letter, out var indexStatus);
            resolvedByDrive.TryGetValue(letter, out var drive);
            item.Id = NetworkDriveResolver.GetNetworkId(letter);
            item.IsPresent = drive != null;
            if (!item.IsPresent) item.IsEnabled = false;
            TrackPendingRebuild(letter, indexStatus?.State);
            item.State = drive == null ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(drive, indexStatus);
            item.ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-";
            configured.TryGetValue(item.Id, out var saved);
            UpdateRowAction(item, item.IsPresent && saved != null, indexStatus?.State);
        }
        foreach (var name in visibleWsl)
        {
            var item = WslDrives.FirstOrDefault(d => d.DistroName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (item == null) continue;
            var unc = $@"\\wsl$\{name}";
            statuses.TryGetValue(unc, out var indexStatus);
            var isPresent = wslDistros.Contains(name, StringComparer.OrdinalIgnoreCase);
            item.IsPresent = isPresent;
            if (!item.IsPresent) item.IsEnabled = false;
            TrackPendingRebuild(unc, indexStatus?.State);
            item.State = !isPresent ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(null, indexStatus);
            item.ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-";
            configuredWsl.TryGetValue(name, out var saved);
            UpdateWslRowAction(item, isPresent && saved != null, indexStatus?.State);
        }
        foreach (var path in visibleFolders)
        {
            var item = FolderIndexes.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (item == null) continue;
            statuses.TryGetValue(path, out var indexStatus);
            var isPresent = Directory.Exists(path);
            item.IsPresent = isPresent;
            if (!isPresent) item.IsEnabled = false;
            TrackPendingRebuild(path, indexStatus?.State);
            item.State = !isPresent ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(null, indexStatus);
            item.ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-";
            configuredFolders.TryGetValue(path, out var saved);
            UpdateFolderRowAction(item, isPresent && saved != null, indexStatus?.State);
        }
    }

    private void RebuildRows(
        List<string> visibleDrives, List<string> visibleWsl, List<string> visibleFolders,
        Dictionary<string, NetworkIndexStatus> statuses, Dictionary<string, ResolvedNetworkDrive> resolvedByDrive, List<string> wslDistros,
        Dictionary<string, NetworkDriveSetting> configured, Dictionary<string, WslSetting> configuredWsl, Dictionary<string, FolderIndexSetting> configuredFolders)
    {
        foreach (var existing in NetworkDrives) existing.PropertyChanged -= OnNetworkDriveItemChanged;
        NetworkDrives.Clear();
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
            item.RowActionCommand = new RelayCommand(() => NetworkDriveViewModelHelper.RunDriveAction(item, this, _userSettings, _searchService, _onTriggerFastRefresh, _pendingRowRebuilds, _observedRowRebuilds), () => item.CanRunRowAction);
            TrackPendingRebuild(letter, indexStatus?.State);
            UpdateRowAction(item, drive != null && saved != null, indexStatus?.State);
            item.PropertyChanged += OnNetworkDriveItemChanged;
            NetworkDrives.Add(item);
        }

        foreach (var existing in WslDrives) existing.PropertyChanged -= OnWslDriveItemChanged;
        WslDrives.Clear();
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
            item.RowActionCommand = new RelayCommand(() => NetworkDriveViewModelHelper.RunWslDriveAction(item, this, _userSettings, _searchService, _onTriggerFastRefresh, _pendingRowRebuilds, _observedRowRebuilds), () => item.CanRunRowAction);
            TrackPendingRebuild(unc, indexStatus?.State);
            UpdateWslRowAction(item, isPresent && saved != null, indexStatus?.State);
            item.PropertyChanged += OnWslDriveItemChanged;
            WslDrives.Add(item);
        }

        foreach (var existing in FolderIndexes) existing.PropertyChanged -= OnFolderItemChanged;
        FolderIndexes.Clear();
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
            item.RowActionCommand = new RelayCommand(() => NetworkDriveViewModelHelper.RunFolderIndexAction(item, this, _userSettings, _searchService, _onTriggerFastRefresh, _pendingRowRebuilds, _observedRowRebuilds), () => item.CanRunRowAction);
            TrackPendingRebuild(path, indexStatus?.State);
            UpdateFolderRowAction(item, isPresent && saved != null, indexStatus?.State);
            item.PropertyChanged += OnFolderItemChanged;
            FolderIndexes.Add(item);
        }
    }
}
