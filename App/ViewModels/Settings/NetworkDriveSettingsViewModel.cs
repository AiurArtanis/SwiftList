using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.Core;
using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Settings.NetworkDrive;

namespace SwiftList.App.ViewModels.Settings;

public class NetworkDriveSettingsViewModel : ViewModelBase
{
    private readonly SearchService _searchService;
    private readonly Action _onTriggerFastRefresh;
    private readonly HashSet<string> _pendingRowRebuilds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _observedRowRebuilds = new(StringComparer.OrdinalIgnoreCase);
    private string _networkIndexSummary = TranslationManager.Instance["Network_SummaryBusy"];
    private string _wslIndexSummary = TranslationManager.Instance["Network_SummaryBusy"];
    private string _folderIndexSummary = TranslationManager.Instance["Network_SummaryBusy"];
    private bool _canRebuildDrives;
    private bool _canRebuildWsl;
    private bool _canRebuildFolders;
    private bool _canAddFolder = true;
    private bool _isNetworkDrivesEmpty;
    private string _drivesPlaceholderText = string.Empty;
    private bool _hasPendingEdits;
    private ICommand? _addFolderCommand;
    private readonly LabeledOption[] _refreshModeOptions;

    public NetworkDriveSettingsViewModel(SearchService searchService, Action onTriggerFastRefresh)
    {
        _searchService = searchService;
        _onTriggerFastRefresh = onTriggerFastRefresh;
        RebuildDrivesCommand = new RelayCommand(
            () => NetworkDriveViewModelHelper.RebuildDrives(this, _searchService, _onTriggerFastRefresh),
            () => CanRebuildDrives);
        RebuildWslCommand = new RelayCommand(
            () => NetworkDriveViewModelHelper.RebuildWsl(this, _searchService, _onTriggerFastRefresh),
            () => CanRebuildWsl);
        RebuildFoldersCommand = new RelayCommand(
            () => NetworkDriveViewModelHelper.RebuildFolders(this, _searchService, _onTriggerFastRefresh),
            () => CanRebuildFolders);

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
            foreach (var item in FolderIndexes)
            {
                item.NotifyLanguageChanged();
            }
        };
    }

    public ObservableCollection<NetworkDriveSettingsItem> NetworkDrives { get; } = new();
    public ObservableCollection<WslSettingsItem> WslDrives { get; } = new();
    public ObservableCollection<FolderIndexSettingsItem> FolderIndexes { get; } = new();
    public bool IsFolderIndexesEmpty => FolderIndexes.Count == 0;
    // Companion bool for XAML Visibility bindings that need the opposite of IsFolderIndexesEmpty --
    // there's no inverting BoolToVisibilityConverter registered in IndexSettingsPage.xaml.
    public bool HasFolderIndexes => !IsFolderIndexesEmpty;

    public ICommand AddFolderCommand => _addFolderCommand ??= new RelayCommand(
        () => NetworkDriveFolderHelper.AddFolder(this, _searchService, _onTriggerFastRefresh, _pendingRowRebuilds, _observedRowRebuilds));

    // Called from NetworkDriveViewModelHelper.RunFolderIndexAction's Delete branch -- removes the row
    // from view entirely (not just resetting its RowAction, since there's nothing left to show for it).
    internal void RemoveFolderIndex(FolderIndexSettingsItem item)
    {
        item.PropertyChanged -= OnFolderItemChanged;
        FolderIndexes.Remove(item);
        OnPropertyChanged(nameof(IsFolderIndexesEmpty));
    }

    internal void NotifyFolderIndexesEmptyChanged() => OnPropertyChanged(nameof(IsFolderIndexesEmpty));

    public bool HasPendingEdits { get => _hasPendingEdits; internal set => SetProperty(ref _hasPendingEdits, value); }
    public bool IsWslPanelVisible => WslDrives.Count > 0;

    public IReadOnlyList<LabeledOption> RefreshModeOptions => _refreshModeOptions;

    // Each of NetworkDrives/WslDrives/FolderIndexes gets its own Rebuild command, summary text, and
    // enablement -- these three categories share this one ViewModel/page but their scan state, item
    // counts, and busy-ness must never bleed into each other's display or actions.
    public ICommand RebuildDrivesCommand { get; }
    public ICommand RebuildWslCommand { get; }
    public ICommand RebuildFoldersCommand { get; }

    public string NetworkIndexSummary { get => _networkIndexSummary; set => SetProperty(ref _networkIndexSummary, value); }
    public string WslIndexSummary { get => _wslIndexSummary; set => SetProperty(ref _wslIndexSummary, value); }
    public string FolderIndexSummary { get => _folderIndexSummary; set => SetProperty(ref _folderIndexSummary, value); }

    public bool CanRebuildDrives
    {
        get => _canRebuildDrives;
        set { if (SetProperty(ref _canRebuildDrives, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public bool CanRebuildWsl
    {
        get => _canRebuildWsl;
        set { if (SetProperty(ref _canRebuildWsl, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public bool CanRebuildFolders
    {
        get => _canRebuildFolders;
        set { if (SetProperty(ref _canRebuildFolders, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    // Deliberately just !folderBusy, not CanRebuildFolders itself -- CanRebuildFolders is also false
    // whenever nothing is AppliedEnabled yet (e.g. a folder just added and not applied), which would
    // disable Add right after adding your first folder, before you'd ever get a chance to add a second one.
    public bool CanAddFolder { get => _canAddFolder; private set => SetProperty(ref _canAddFolder, value); }

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
        // Specifically "\\wsl$\..."/"\\wsl.localhost\...", not every "\\"-prefixed cached key -- a real
        // UNC share cached via the folder-index feature ("\\server\share") must not get folded in here
        // just for sharing the same leading "\\", which would show it as a fake WSL distro (and risk a
        // name collision if a real distro happens to share the share's leaf name).
        var cachedWslDrives = _searchService.GetCachedNetworkDrives()
            .Where(NetworkDriveSettingsHelper.IsWslPath)
            .Select(d => System.IO.Path.GetFileName(d.TrimEnd('\\')))
            .ToList();
        var visibleWsl = wslDistros
            .Concat(cachedWslDrives)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var visibleFolders = NetworkDriveFolderHelper.GetVisibleFolders(this, _searchService, userSettings);

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
            NetworkDriveFolderHelper.UpdateRowsInPlace(this, _searchService, visibleDrives, visibleWsl, visibleFolders, statuses, resolvedByDrive, wslDistros, configured, configuredWsl, configuredFolders);
        else
            NetworkDriveFolderHelper.RebuildRows(this, _searchService, _onTriggerFastRefresh, _pendingRowRebuilds, _observedRowRebuilds, visibleDrives, visibleWsl, visibleFolders, statuses, resolvedByDrive, wslDistros, configured, configuredWsl, configuredFolders);

        // Scoped to NetworkDrives alone -- this used to require every category empty at once, so the
        // "no network drives" placeholder never showed as long as some unrelated folder or WSL distro was
        // configured, leaving the Network tab's own list looking like a headers-only blank.
        IsNetworkDrivesEmpty = NetworkDrives.Count == 0;
        DrivesPlaceholderText = TranslationManager.Instance["Network_Placeholder"];

        // Per-category busy, so an indexing folder can't disable a network drive's row controls (or the
        // reverse) just because this used to check one indexStatuses list combined across all three.
        // isGlobalBusy (the elevated local USN service's reachability) still applies to drives/WSL as
        // before -- only folders exclude it, since folder indexing never goes through that service.
        var driveBusy = isGlobalBusy || NetworkDrivePermissionsHelper.IsCategoryBusy(_pendingRowRebuilds, NetworkDrives.Select(d => d.Drive), indexStatuses);
        var wslBusy = isGlobalBusy || NetworkDrivePermissionsHelper.IsCategoryBusy(_pendingRowRebuilds, WslDrives.Select(w => $@"\\wsl$\{w.DistroName}"), indexStatuses);
        var folderBusy = NetworkDrivePermissionsHelper.IsCategoryBusy(_pendingRowRebuilds, FolderIndexes.Select(f => f.Path), indexStatuses);
        CanRebuildDrives = NetworkDrives.Any(d => d.AppliedEnabled) && !driveBusy;
        CanRebuildWsl = WslDrives.Any(w => w.AppliedEnabled) && !wslBusy;
        CanRebuildFolders = FolderIndexes.Any(f => f.AppliedEnabled) && !folderBusy;
        CanAddFolder = !folderBusy;
        NetworkDrivePermissionsHelper.UpdateRowPermissions(this, driveBusy, wslBusy, folderBusy);
        NetworkDriveSummaryHelper.UpdateSummaries(this, indexStatuses, driveBusy, wslBusy, folderBusy);

        OnPropertyChanged(nameof(IsWslPanelVisible));
        OnPropertyChanged(nameof(IsFolderIndexesEmpty));
        OnPropertyChanged(nameof(HasFolderIndexes));
    }

    public void ResetPendingEdits() => HasPendingEdits = false;

    internal void OnNetworkDriveItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetworkDriveSettingsItem.IsEnabled) or nameof(NetworkDriveSettingsItem.RefreshMode)) HasPendingEdits = true;
    }

    internal void OnWslDriveItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WslSettingsItem.IsEnabled) or nameof(WslSettingsItem.RefreshMode)) HasPendingEdits = true;
    }

    internal void OnFolderItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FolderIndexSettingsItem.IsEnabled) or nameof(FolderIndexSettingsItem.RefreshMode)) HasPendingEdits = true;
    }

    // appliedEnabled is always recomputed by the caller from the current UserSettings.NetworkDrives/
    // WslSettings (the same way LocalDriveSettingsViewModel.UpdateStatus derives its own local appliedEnabled
    // every call), never read back off a previously-stored field -- so RowAction can't go stale just because
    // some save path forgot to sync a cached "applied" flag afterwards.
    // Shared by all three row categories -- only what counts as "eligible for Delete once un-applied"
    // differs: a drive/WSL row only if the scan cache still remembers its key, a folder row always
    // (see NetworkDriveFolderHelper's caller for why).
    internal void UpdateRowAction<TItem>(TItem item, bool appliedEnabled, string? state, Func<TItem, bool> canDeleteWhenUnapplied) where TItem : INetworkRowItem
    {
        item.AppliedEnabled = appliedEnabled;
        item.RowAction = appliedEnabled
            ? (state == "indexing" ? NetworkDriveRowAction.Stop : NetworkDriveRowAction.Rebuild)
            : canDeleteWhenUnapplied(item) ? NetworkDriveRowAction.Delete : NetworkDriveRowAction.None;
    }

    internal void TrackPendingRebuild(string drive, string? state)
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
