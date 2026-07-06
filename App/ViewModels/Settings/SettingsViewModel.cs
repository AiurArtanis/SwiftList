using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;
using SwiftList.Core.Indexer.Usn;
using SwiftList.Core.Indexer.NetworkDrive;
using System.ComponentModel;
using SwiftList.App.ViewModels.Settings.Plugins;
namespace SwiftList.App.ViewModels.Settings;

public class SettingsViewModel : ViewModelBase
{
    private readonly SearchService _searchService = new();
    private readonly UserSettings _userSettings = UserSettings.Load();
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _statusSubscriptionCts = new();
    private Task? _statusSubscriptionTask;
    private bool _canApply = true;
    private bool _isBusy;
    private bool _isServiceReady = true;
    private UsnIndexer.IndexerStatus _latestStatus = new() { State = "error" };
    private IReadOnlyList<NetworkIndexStatus> _latestNetworkStatuses = Array.Empty<NetworkIndexStatus>();
    private MachineSettings _latestMachineSettings = new();

    public SettingsViewModel()
    {
        Service = new ServiceSettingsViewModel(_searchService, RefreshLists);

        LocalDrive = new LocalDriveSettingsViewModel(_searchService, RefreshLists);

        NetworkDrive = new NetworkDriveSettingsViewModel(_searchService, _userSettings, RefreshLists);
        General = new GeneralSettingsViewModel(_userSettings);
        Exclusions = new ExclusionSettingsViewModel(_userSettings);
        Plugins = new PluginManagementViewModel(_userSettings);
        Hotkeys = new HotkeySettingsViewModel(_userSettings);
        Blacklist = new BlacklistSettingsViewModel(_userSettings);
        History = new HistorySettingsViewModel(_userSettings);
        Favorites = new FavoritesSettingsViewModel(_userSettings);
        RefreshCommand = new RelayCommand(Refresh);
        ApplyCommand = new RelayCommand(Apply, () => CanApply);

        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += (s, e) => RefreshLists();
        _refreshTimer.Start();
        UserNetworkDriveSearch.StatusesChanged += OnNetworkStatusesChanged;
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        EnsureStatusSubscription();
        RefreshLists();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => ApplyUiState();

    public ServiceSettingsViewModel Service { get; }
    public LocalDriveSettingsViewModel LocalDrive { get; }
    public NetworkDriveSettingsViewModel NetworkDrive { get; }
    public GeneralSettingsViewModel General { get; }
    public ExclusionSettingsViewModel Exclusions { get; }
    public PluginManagementViewModel Plugins { get; }
    public HotkeySettingsViewModel Hotkeys { get; }
    public BlacklistSettingsViewModel Blacklist { get; }
    public HistorySettingsViewModel History { get; }
    public FavoritesSettingsViewModel Favorites { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ApplyCommand { get; }

    public bool CanApply
    {
        get => _canApply;
        set { if (SetProperty(ref _canApply, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
    public bool IsServiceReady { get => _isServiceReady; set => SetProperty(ref _isServiceReady, value); }

    public void Cleanup()
    {
        _refreshTimer?.Stop();
        _statusSubscriptionCts.Cancel();
        UserNetworkDriveSearch.StatusesChanged -= OnNetworkStatusesChanged;
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
    }

    public void Refresh() => RefreshLists();

    public void RefreshLists() => _ = Task.Run(async () =>
    {
        MachineSettings settings;
        var isServiceReady = false;

        try
        {
            isServiceReady = await _searchService.PingAsync();
            if (isServiceReady)
            {
                settings = await _searchService.GetMachineSettingsAsync();
                _latestNetworkStatuses = _searchService.GetNetworkIndexStatuses();
            }
            else
            {
                settings = new MachineSettings();
                _latestNetworkStatuses = Array.Empty<NetworkIndexStatus>();
            }
        }
        catch
        {
            settings = new MachineSettings();
            _latestNetworkStatuses = Array.Empty<NetworkIndexStatus>();
        }

        _latestMachineSettings = settings;
        if (!isServiceReady)
            _latestStatus = new UsnIndexer.IndexerStatus { State = "error" };

        EnsureStatusSubscription();
        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(ApplyUiState));
    });

    public void Apply()
    {
        if (!CanApply)
            return;

        var previousNetworkDrives = _userSettings.NetworkDrives
            .Select(d => new NetworkDriveSetting { Id = d.Id, RefreshMode = d.RefreshMode })
            .ToList();
        var previousWslDrives = _userSettings.WslSettings
            .Select(w => new WslSetting { Id = w.Id, RefreshMode = w.RefreshMode })
            .ToList();
        var previousExclusions = SettingsChangeSnapshot.CaptureExclusions(_userSettings);
        var previousDisabledAliases = _userSettings.DisabledPluginComponents
            .Where(c => c.Contains("::AliasProvider::", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var machineSettings = new MachineSettings
        {
            LocalDrives = LocalDrive.LocalDrives.Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Id)).Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        var newNetworkDrives = NetworkDrive.NetworkDrives.Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Id)).Select(d => new NetworkDriveSetting
        {
            Id = d.Id,
            RefreshMode = d.RefreshMode
        }).ToList();
        var newWslDrives = NetworkDrive.WslDrives.Where(w => w.IsEnabled && !string.IsNullOrWhiteSpace(w.Id)).Select(w => new WslSetting
        {
            Id = w.Id,
            RefreshMode = w.RefreshMode
        }).ToList();
        var localDriveSnapshots = LocalDrive.LocalDrives
            .Select(d => new LocalDriveSnapshot(d.Drive, d.Id, d.IsEnabled))
            .ToList();
        _userSettings.NetworkDrives = newNetworkDrives;
        _userSettings.WslSettings = newWslDrives;
        Exclusions.Save();
        General.Apply();
        Plugins.Save();
        Hotkeys.Apply();
        Blacklist.Save();
        History.Save();
        Favorites.Save();
        _userSettings.Save();
        App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
        PluginManager.Instance.RefreshDisabledComponents();
        NetworkDrive.ResetPendingEdits();
        var exclusionsChanged = SettingsChangeSnapshot.ExclusionsChanged(previousExclusions, SettingsChangeSnapshot.CaptureExclusions(_userSettings));
        var newDisabledAliases = _userSettings.DisabledPluginComponents
            .Where(c => c.Contains("::AliasProvider::", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var aliasProviderEnabled = previousDisabledAliases.Any(c => !newDisabledAliases.Contains(c, StringComparer.OrdinalIgnoreCase));

        _ = Task.Run(async () =>
        {
            var previousLocalDrives = (await _searchService.GetMachineSettingsAsync()).LocalDrives.ToList();
            if (SettingsChangeSnapshot.StringListChanged(previousLocalDrives, machineSettings.LocalDrives))
                await _searchService.SaveMachineSettingsAsync(machineSettings);

            if (exclusionsChanged)
            {
                _searchService.RefreshNetworkIndexes();
            }
            else if (NetworkSettingsChanged(previousNetworkDrives, newNetworkDrives) || WslSettingsChanged(previousWslDrives, newWslDrives))
            {
                await NetworkDriveApplyHelper.ApplyChangesAsync(_searchService, previousNetworkDrives, newNetworkDrives);
                foreach (var wsl in newWslDrives)
                {
                    if (!previousWslDrives.Any(w => w.Id.Equals(wsl.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        var unc = $@"\\wsl$\{wsl.Id}";
                        _searchService.RefreshNetworkDriveIndex(unc);
                    }
                }
            }

            if (exclusionsChanged)
                await RebuildScanBasedLocalDrivesAsync(localDriveSnapshots, machineSettings.LocalDrives);

            if (aliasProviderEnabled)
                await _searchService.InitializeOrLoadIndexAsync(false);

            RefreshLists();
        });
    }

    private void EnsureStatusSubscription()
    {
        if (_statusSubscriptionTask is { IsCompleted: false })
            return;

        _statusSubscriptionTask = StartStatusSubscriptionAsync(_statusSubscriptionCts.Token);
    }

    private async Task StartStatusSubscriptionAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        try
        {
            await SearchStatusStream.SubscribeAsync(status =>
            {
                _latestStatus = status;
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(ApplyUiState));
            }, token).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void OnNetworkStatusesChanged(IReadOnlyList<NetworkIndexStatus> statuses)
    {
        _latestNetworkStatuses = statuses;
        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(ApplyUiState));
    }

    private void ApplyUiState()
    {
        var status = _latestStatus;
        var settings = _latestMachineSettings;
        var networkStatuses = _latestNetworkStatuses;
        var isServiceReady = status.State != "error";
        var isLocalDriveBusy = status.Drives.Any(d => d.State is "indexing" or "pending");
        var isNetworkBusy = networkStatuses.Any(s => s.State is "indexing" or "pending");
        // "indexing"/"pending" are set on this shared status both for the real startup scan AND for a
        // later on-demand single-drive rebuild (SearchEngineDriveMaintenance.ForceRebuildDrive) -- the
        // two are indistinguishable from status.State alone. Only "loading-cache" is unambiguous (it only
        // ever happens once, before the settings page's own data -- local drives, machine settings -- has
        // even loaded, so nothing is safe to save yet); "indexing"/"pending"/IsMaintenanceBusy always
        // coincide with isLocalDriveBusy being true too, so treat those as local-drive-specific busy-ness,
        // not general service-lifecycle busy-ness.
        var isServiceLifecycleBusy = status.State == "loading-cache"
            || (!isLocalDriveBusy && (status.State is "indexing" or "pending" || status.IsMaintenanceBusy));
        Service.UpdateStatus(status);
        // Each side's own panel is gated ONLY on the service's lifecycle state plus its own busy-ness --
        // local indexing must never disable network's controls and vice versa (LocalDrive.UpdateStatus
        // below already only looks at local `status`; NetworkDriveSettingsViewModel.RefreshNetworkDrives
        // separately layers in its own isNetworkBusy-equivalent check from `networkStatuses` internally).
        LocalDrive.UpdateStatus(status, settings);
        NetworkDrive.RefreshNetworkDrives(_userSettings, networkStatuses, isServiceLifecycleBusy);
        // The shared Apply/OK button is different: it commits both sides' settings in one shot, so it
        // should stay disabled while EITHER side is busy, even though neither side blocks the other's own
        // panel controls.
        var isBusy = isServiceLifecycleBusy || isLocalDriveBusy || isNetworkBusy;
        IsServiceReady = isServiceReady;
        IsBusy = isBusy;
        CanApply = !isBusy;
    }

    private async Task RebuildScanBasedLocalDrivesAsync(IReadOnlyList<LocalDriveSnapshot> drives, IReadOnlyList<string> enabledLocalDriveIds)
    {
        var enabled = enabledLocalDriveIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in drives.Where(d => d.IsEnabled && (enabled.Count == 0 || enabled.Contains(d.Id))))
        {
            var fs = VolumeHelper.GetFileSystemType(drive.Drive);
            if (!fs.Equals("NTFS", StringComparison.OrdinalIgnoreCase) &&
                await _searchService.RebuildDriveIndexAsync(drive.Drive))
                await WaitForLocalDriveRebuildAsync(drive.Drive);
        }
    }

    private async Task WaitForLocalDriveRebuildAsync(string drive)
    {
        for (var i = 0; i < 120; i++)
        {
            await Task.Delay(500);
            var status = await _searchService.GetStatusAsync();
            var item = status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
            if (item?.State is not ("pending" or "indexing"))
                return;
        }
    }

    private static bool NetworkSettingsChanged(IReadOnlyList<NetworkDriveSetting> oldSettings, IReadOnlyList<NetworkDriveSetting> newSettings)
    {
        var oldOrdered = oldSettings
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => $"{d.Id}|{d.RefreshMode}");

        var newOrdered = newSettings
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => $"{d.Id}|{d.RefreshMode}");
        return !oldOrdered.SequenceEqual(newOrdered, StringComparer.OrdinalIgnoreCase);
    }

    private static bool WslSettingsChanged(IReadOnlyList<WslSetting> oldSettings, IReadOnlyList<WslSetting> newSettings)
    {
        var oldOrdered = oldSettings
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => $"{d.Id}|{d.RefreshMode}");

        var newOrdered = newSettings
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => $"{d.Id}|{d.RefreshMode}");
        return !oldOrdered.SequenceEqual(newOrdered, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record LocalDriveSnapshot(string Drive, string Id, bool IsEnabled);
}
