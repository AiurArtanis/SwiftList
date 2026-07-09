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
        Log = new ServiceLogViewModel(_searchService);

        LocalDrive = new LocalDriveSettingsViewModel(_searchService, RefreshLists);

        NetworkDrive = new NetworkDriveSettingsViewModel(_searchService, _userSettings, RefreshLists);
        General = new GeneralSettingsViewModel(_userSettings);
        Exclusions = new ExclusionSettingsViewModel(_userSettings);
        Plugins = new PluginManagementViewModel(_userSettings);
        Blacklist = new BlacklistSettingsViewModel(_userSettings);
        Hotkeys = new HotkeySettingsViewModel(_userSettings, Blacklist);
        History = new HistorySettingsViewModel(_userSettings);
        Favorites = new FavoritesSettingsViewModel(_userSettings);
        StartupPanel = new StartupPanelSettingsViewModel(_userSettings);
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
    public ServiceLogViewModel Log { get; }
    public LocalDriveSettingsViewModel LocalDrive { get; }
    public NetworkDriveSettingsViewModel NetworkDrive { get; }
    public GeneralSettingsViewModel General { get; }
    public ExclusionSettingsViewModel Exclusions { get; }
    public PluginManagementViewModel Plugins { get; }
    public HotkeySettingsViewModel Hotkeys { get; }
    public BlacklistSettingsViewModel Blacklist { get; }
    public HistorySettingsViewModel History { get; }
    public FavoritesSettingsViewModel Favorites { get; }
    public StartupPanelSettingsViewModel StartupPanel { get; }
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
        Log.Dispose();
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
        var previousFolderIndexes = _userSettings.FolderIndexes
            .Select(f => new FolderIndexSetting { Path = f.Path, RefreshMode = f.RefreshMode })
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
        var newFolderIndexes = NetworkDrive.FolderIndexes.Where(f => f.IsEnabled && !string.IsNullOrWhiteSpace(f.Path)).Select(f => new FolderIndexSetting
        {
            Path = f.Path,
            RefreshMode = f.RefreshMode
        }).ToList();
        var localDriveSnapshots = LocalDrive.LocalDrives
            .Select(d => new LocalDriveSnapshot(d.Drive, d.Id, d.IsEnabled))
            .ToList();
        _userSettings.NetworkDrives = newNetworkDrives;
        _userSettings.WslSettings = newWslDrives;
        _userSettings.FolderIndexes = newFolderIndexes;
        Exclusions.Save();
        General.Apply();
        Plugins.Save();
        Hotkeys.Apply();
        Blacklist.Save();
        History.Save();
        Favorites.Save();
        StartupPanel.Save();
        _userSettings.Save();
        App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
        PluginManager.Instance.RefreshDisabledComponents();
        StartupPanel.RefreshPluginTabs();
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
            else if (SettingsApplyHelpers.NetworkSettingsChanged(previousNetworkDrives, newNetworkDrives)
                || SettingsApplyHelpers.WslSettingsChanged(previousWslDrives, newWslDrives)
                || SettingsApplyHelpers.FolderIndexesChanged(previousFolderIndexes, newFolderIndexes))
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
                // Unlike a network drive, a folder path never needs resolving from the OS, so there's
                // nothing to wait for -- ConfigureNetworkIndexes() (already called above via
                // ApplyChangesAsync) already auto-queues an initial refresh for it; this just requests it
                // directly, same as a newly-added WSL distro above.
                foreach (var folder in newFolderIndexes)
                {
                    if (!previousFolderIndexes.Any(f => f.Path.Equals(folder.Path, StringComparison.OrdinalIgnoreCase)))
                        _searchService.RefreshNetworkDriveIndex(folder.Path);
                }
            }

            if (exclusionsChanged)
                await SettingsApplyHelpers.RebuildScanBasedLocalDrivesAsync(_searchService, localDriveSnapshots, machineSettings.LocalDrives);

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
        Service.UpdateStatus(status);
        LocalDrive.UpdateStatus(status, settings);
        // Network settings come from UserSettings.Load() (a separate local file, read once at startup)
        // and network indexing is its own subsystem -- neither depends on the local USN indexer's own
        // lifecycle. The only thing that legitimately blocks network settings from a "service"
        // perspective is not being able to reach the service at all.
        NetworkDrive.RefreshNetworkDrives(_userSettings, networkStatuses, !isServiceReady);
        // The WSL tab hides itself once its drive list empties out (e.g. the last distro was removed).
        // If it was the active tab, fall back to Network so the page never lands on a hidden tab.
        if (LocalDrive.SelectedTab == "Wsl" && !NetworkDrive.IsWslPanelVisible)
            LocalDrive.SelectedTab = "Network";
        // The shared Apply/OK button only needs the service to be reachable: MachineSettings is loaded
        // synchronously at SearchEngine construction, before the indexer's own loading-cache/indexing/
        // pending lifecycle even starts, so an active scan or cache load never means the data Apply()
        // would read and save is stale or empty -- only an unreachable service does (RefreshLists()
        // falls back to an empty MachineSettings() in that case).
        IsServiceReady = isServiceReady;
        Log.IsServiceReady = isServiceReady;
        IsBusy = !isServiceReady;
        CanApply = isServiceReady;
    }

}
