using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.Core;
using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Settings;

public class NetworkDriveSettingsViewModel : ViewModelBase
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

        // Update in place (don't Clear+rebuild) whenever the drive/WSL set is unchanged. A periodic status
        // refresh rebuilding the rows would replace the WslSettingsItem a "refresh mode" ComboBox is bound
        // to and instantly close its open dropdown -- which is why the WSL refresh mode couldn't be changed
        // once indexing started producing status. Only rebuild when a drive/distro is actually added/removed.
        var structureUnchanged =
            NetworkDrives.Count == visibleDrives.Count &&
            visibleDrives.All(letter => NetworkDrives.Any(d => d.Drive.Equals(letter, StringComparison.OrdinalIgnoreCase))) &&
            WslDrives.Count == visibleWsl.Count &&
            visibleWsl.All(name => WslDrives.Any(d => d.DistroName.Equals(name, StringComparison.OrdinalIgnoreCase)));

        if (HasPendingEdits || structureUnchanged)
        {
            foreach (var letter in visibleDrives)
            {
                var item = NetworkDrives.FirstOrDefault(d => d.Drive.Equals(letter, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
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
            }
            foreach (var name in visibleWsl)
            {
                var item = WslDrives.FirstOrDefault(d => d.DistroName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
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
            }
        }
        else
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
        }

        IsNetworkDrivesEmpty = NetworkDrives.Count == 0 && WslDrives.Count == 0;
        DrivesPlaceholderText = TranslationManager.Instance["Network_Placeholder"];

        var hasEnabled = NetworkDrives.Any(d => d.AppliedEnabled) || WslDrives.Any(w => w.AppliedEnabled);
        var isBusy = isGlobalBusy || _pendingRowRebuilds.Count > 0 || indexStatuses?.Any(s => s.State == "indexing" || s.State == "pending") == true;
        _isBusy = isBusy;
        CanRebuild = hasEnabled && !isBusy;
        CanEditRefreshModes = !isBusy;
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

        if (IsNetworkDrivesEmpty)
        {
            IndexSummary = TranslationManager.Instance["Network_DrivesEmpty"];
        }
        else
        {
            var enabledCount = NetworkDrives.Count(d => d.AppliedEnabled) + WslDrives.Count(w => w.AppliedEnabled);
            var totalItems = (indexStatuses ?? Array.Empty<NetworkIndexStatus>()).Sum(s => s.Items);
            var state = isBusy ? TranslationManager.Instance["Network_StatusIndexing"] : TranslationManager.Instance["Status_Ready"];
            IndexSummary = string.Format(TranslationManager.Instance["Network_SummaryTemplate"], state, enabledCount, totalItems);
        }
        OnPropertyChanged(nameof(IsWslPanelVisible));
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
