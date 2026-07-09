using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.Core;
using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Settings;

public partial class NetworkDriveSettingsViewModel : ViewModelBase
{
    private readonly SearchService _searchService;
    private readonly UserSettings _userSettings;
    private readonly Action _onTriggerFastRefresh;
    private readonly HashSet<string> _pendingRowRebuilds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _observedRowRebuilds = new(StringComparer.OrdinalIgnoreCase);
    private string _indexSummary = TranslationManager.Instance["Network_SummaryBusy"];
    private bool _canRebuild;
    private bool _isNetworkDrivesEmpty;
    private string _drivesPlaceholderText = string.Empty;
    private bool _hasPendingEdits;
    private bool _canEditRefreshModes = true;
    private bool _isBusy;
    private readonly LabeledOption[] _refreshModeOptions;

    public NetworkDriveSettingsViewModel(SearchService searchService, UserSettings userSettings, Action onTriggerFastRefresh)
    {
        _searchService = searchService;
        _userSettings = userSettings;
        _onTriggerFastRefresh = onTriggerFastRefresh;
        RebuildCommand = new RelayCommand(
            () => NetworkDriveViewModelHelper.Rebuild(this, _userSettings, _searchService, _onTriggerFastRefresh),
            () => CanRebuild);

        _refreshModeOptions =
        [
            new LabeledOption("Manual", TranslationManager.Instance["Network_ModeManual"]),
            new LabeledOption("15Minutes", TranslationManager.Instance["Network_Mode15M"]),
            new LabeledOption("Hourly", TranslationManager.Instance["Network_ModeHourly"]),
            new LabeledOption("Daily", TranslationManager.Instance["Network_ModeDaily"])
        ];

        TranslationManager.Instance.PropertyChanged += (s, e) =>
        {
            // Update labels in place -- the ComboBoxes' ItemsSource is bound to this same stable
            // array/reference and never gets reassigned, so their SelectedValue is never disturbed.
            _refreshModeOptions[0].Label = TranslationManager.Instance["Network_ModeManual"];
            _refreshModeOptions[1].Label = TranslationManager.Instance["Network_Mode15M"];
            _refreshModeOptions[2].Label = TranslationManager.Instance["Network_ModeHourly"];
            _refreshModeOptions[3].Label = TranslationManager.Instance["Network_ModeDaily"];

            foreach (var item in NetworkDrives)
            {
                item.NotifyLanguageChanged();
            }
            foreach (var item in WslDrives)
            {
                item.NotifyLanguageChanged();
            }
        };
    }

    public ObservableCollection<NetworkDriveSettingsItem> NetworkDrives { get; } = new();
    public ObservableCollection<WslSettingsItem> WslDrives { get; } = new();

    public bool HasPendingEdits { get => _hasPendingEdits; private set => SetProperty(ref _hasPendingEdits, value); }
    public bool CanEditRefreshModes { get => _canEditRefreshModes; private set => SetProperty(ref _canEditRefreshModes, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
    public bool IsWslPanelVisible => WslDrives.Count > 0;

    public IReadOnlyList<LabeledOption> RefreshModeOptions => _refreshModeOptions;

    public ICommand RebuildCommand { get; }
    public string IndexSummary { get => _indexSummary; set => SetProperty(ref _indexSummary, value); }

    public bool CanRebuild
    {
        get => _canRebuild;
        set { if (SetProperty(ref _canRebuild, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsNetworkDrivesEmpty { get => _isNetworkDrivesEmpty; set => SetProperty(ref _isNetworkDrivesEmpty, value); }
    public string DrivesPlaceholderText { get => _drivesPlaceholderText; set => SetProperty(ref _drivesPlaceholderText, value); }

    public void RefreshNetworkDrives(UserSettings userSettings, IReadOnlyList<NetworkIndexStatus>? indexStatuses = null, bool isGlobalBusy = false)
    {
        var configured = userSettings.NetworkDrives
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var configuredWsl = userSettings.WslSettings
            .Where(w => !string.IsNullOrWhiteSpace(w.Id))
            .ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);
        var configuredFolders = userSettings.FolderIndexes
            .Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);

        var statuses = (indexStatuses ?? Array.Empty<NetworkIndexStatus>())
            .ToDictionary(s => s.Drive, StringComparer.OrdinalIgnoreCase);

        var resolvedDrives = NetworkDriveResolver.GetNetworkDrives();
        var resolvedByDrive = resolvedDrives.ToDictionary(d => d.Letter, StringComparer.OrdinalIgnoreCase);
        var visibleDrives = resolvedDrives.Select(d => d.Letter)
            .Concat(_searchService.GetCachedNetworkDrives().Where(d => d.Length == 1))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var wslDistros = NetworkDriveSettingsHelper.GetWslDistros();
        var cachedWslDrives = _searchService.GetCachedNetworkDrives()
            .Where(d => d.StartsWith(@"\\"))
            .Select(d => System.IO.Path.GetFileName(d.TrimEnd('\\')))
            .ToList();
        var visibleWsl = wslDistros
            .Concat(cachedWslDrives)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var visibleFolders = GetVisibleFolders(userSettings);

        // Update in place (don't Clear+rebuild) whenever the drive/WSL/folder set is unchanged. A periodic
        // status refresh rebuilding the rows would replace the item a "refresh mode" ComboBox is bound to
        // and instantly close its open dropdown -- which is why the WSL refresh mode couldn't be changed
        // once indexing started producing status. Only rebuild when a drive/distro/folder is actually
        // added or removed.
        var structureUnchanged =
            NetworkDrives.Count == visibleDrives.Count &&
            visibleDrives.All(letter => NetworkDrives.Any(d => d.Drive.Equals(letter, StringComparison.OrdinalIgnoreCase))) &&
            WslDrives.Count == visibleWsl.Count &&
            visibleWsl.All(name => WslDrives.Any(d => d.DistroName.Equals(name, StringComparison.OrdinalIgnoreCase))) &&
            FolderIndexes.Count == visibleFolders.Count &&
            visibleFolders.All(path => FolderIndexes.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));

        if (HasPendingEdits || structureUnchanged)
            UpdateRowsInPlace(visibleDrives, visibleWsl, visibleFolders, statuses, resolvedByDrive, wslDistros, configured, configuredWsl, configuredFolders);
        else
            RebuildRows(visibleDrives, visibleWsl, visibleFolders, statuses, resolvedByDrive, wslDistros, configured, configuredWsl, configuredFolders);

        IsNetworkDrivesEmpty = NetworkDrives.Count == 0 && WslDrives.Count == 0 && FolderIndexes.Count == 0;
        DrivesPlaceholderText = TranslationManager.Instance["Network_Placeholder"];

        var hasEnabled = NetworkDrives.Any(d => d.AppliedEnabled) || WslDrives.Any(w => w.AppliedEnabled) || FolderIndexes.Any(f => f.AppliedEnabled);
        var isBusy = isGlobalBusy || _pendingRowRebuilds.Count > 0 || indexStatuses?.Any(s => s.State == "indexing" || s.State == "pending") == true;
        _isBusy = isBusy;
        CanRebuild = hasEnabled && !isBusy;
        CanEditRefreshModes = !isBusy;
        UpdateRowPermissions(isBusy);

        if (IsNetworkDrivesEmpty)
        {
            IndexSummary = TranslationManager.Instance["Network_DrivesEmpty"];
        }
        else
        {
            var enabledCount = NetworkDrives.Count(d => d.AppliedEnabled) + WslDrives.Count(w => w.AppliedEnabled) + FolderIndexes.Count(f => f.AppliedEnabled);
            var totalItems = (indexStatuses ?? Array.Empty<NetworkIndexStatus>()).Sum(s => s.Items);
            var state = isBusy ? TranslationManager.Instance["Network_StatusIndexing"] : TranslationManager.Instance["Status_Ready"];
            IndexSummary = string.Format(TranslationManager.Instance["Network_SummaryTemplate"], state, enabledCount, totalItems);
        }
        OnPropertyChanged(nameof(IsWslPanelVisible));
        OnPropertyChanged(nameof(IsFolderIndexesEmpty));
    }

    private void UpdateRowPermissions(bool isBusy)
    {
        foreach (var drive in NetworkDrives)
        {
            drive.CanEditEnabled = drive.IsPresent && !isBusy;
            drive.CanEditRefreshMode = drive.IsPresent && !isBusy;
            // Stop stays clickable through isBusy -- a Stop row is exactly what's causing it.
            drive.CanRunRowAction = drive.RowAction == NetworkDriveRowAction.Stop
                || (!isBusy && (drive.RowAction == NetworkDriveRowAction.Delete || CanRebuild && drive.RowAction == NetworkDriveRowAction.Rebuild));
        }
        foreach (var wsl in WslDrives)
        {
            wsl.CanEditEnabled = wsl.IsPresent && !isBusy;
            wsl.CanEditRefreshMode = wsl.IsPresent && !isBusy;
            wsl.CanRunRowAction = wsl.RowAction == NetworkDriveRowAction.Stop
                || (!isBusy && (wsl.RowAction == NetworkDriveRowAction.Delete || CanRebuild && wsl.RowAction == NetworkDriveRowAction.Rebuild));
        }
        foreach (var folder in FolderIndexes)
        {
            folder.CanEditEnabled = folder.IsPresent && !isBusy;
            folder.CanEditRefreshMode = folder.IsPresent && !isBusy;
            // Delete also stays clickable through isBusy here, unlike drives/WSL: isBusy for this whole
            // panel includes isGlobalBusy (!isServiceReady, the *local USN* service), which has nothing
            // to do with folder indexing (it runs entirely in-process, never through that service) --
            // removing a folder row that was never applied/cached must not get blocked by an unrelated
            // service being unreachable.
            folder.CanRunRowAction = folder.RowAction is NetworkDriveRowAction.Stop or NetworkDriveRowAction.Delete
                || (!isBusy && CanRebuild && folder.RowAction == NetworkDriveRowAction.Rebuild);
        }
    }

    public void ResetPendingEdits() => HasPendingEdits = false;

    private void OnNetworkDriveItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetworkDriveSettingsItem.IsEnabled) or nameof(NetworkDriveSettingsItem.RefreshMode)) HasPendingEdits = true;
    }

    private void OnWslDriveItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WslSettingsItem.IsEnabled) or nameof(WslSettingsItem.RefreshMode)) HasPendingEdits = true;
    }

    // appliedEnabled is always recomputed by the caller from the current UserSettings.NetworkDrives/
    // WslSettings (the same way LocalDriveSettingsViewModel.UpdateStatus derives its own local appliedEnabled
    // every call), never read back off a previously-stored field -- so RowAction can't go stale just because
    // some save path forgot to sync a cached "applied" flag afterwards.
    private void UpdateRowAction(NetworkDriveSettingsItem item, bool appliedEnabled, string? state)
    {
        item.AppliedEnabled = appliedEnabled;
        item.RowAction = appliedEnabled
            ? (state == "indexing" ? NetworkDriveRowAction.Stop : NetworkDriveRowAction.Rebuild)
            : _searchService.HasNetworkDriveCache(item.Drive) ? NetworkDriveRowAction.Delete : NetworkDriveRowAction.None;
    }

    private void UpdateWslRowAction(WslSettingsItem item, bool appliedEnabled, string? state)
    {
        item.AppliedEnabled = appliedEnabled;
        item.RowAction = appliedEnabled
            ? (state == "indexing" ? NetworkDriveRowAction.Stop : NetworkDriveRowAction.Rebuild)
            : _searchService.HasNetworkDriveCache(item.UncPath) ? NetworkDriveRowAction.Delete : NetworkDriveRowAction.None;
    }

    private void TrackPendingRebuild(string drive, string? state)
    {
        if (!_pendingRowRebuilds.Contains(drive)) return;
        if (state == "indexing") _observedRowRebuilds.Add(drive);
        else if (_observedRowRebuilds.Contains(drive))
        {
            _pendingRowRebuilds.Remove(drive);
            _observedRowRebuilds.Remove(drive);
        }
    }
}
