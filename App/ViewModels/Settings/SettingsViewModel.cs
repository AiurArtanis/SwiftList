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
    private bool _canApply = true;
    private bool _isBusy;
    private bool _isServiceReady = true;

    public SettingsViewModel()
    {
        Service = new ServiceSettingsViewModel(_searchService, Refresh);

        LocalDrive = new LocalDriveSettingsViewModel(_searchService, () => _refreshTimer?.Interval = TimeSpan.FromMilliseconds(100));

        NetworkDrive = new NetworkDriveSettingsViewModel(_searchService, _userSettings, () => _refreshTimer?.Interval = TimeSpan.FromMilliseconds(100));
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
            Interval = TimeSpan.FromSeconds(5) // Start with slow refresh

        };
        _refreshTimer.Tick += (s, e) => Refresh();
        _refreshTimer.Start();
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        Refresh();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

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

        set
        {
            if (SetProperty(ref _canApply, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public bool IsServiceReady
    {
        get => _isServiceReady;
        set => SetProperty(ref _isServiceReady, value);
    }

    public void Cleanup()
    {
        _refreshTimer?.Stop();
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
    }

    public void Refresh() => _ = Task.Run(async () =>
                                  {
                                      UsnIndexer.IndexerStatus status;
                                      MachineSettings settings;
                                      IReadOnlyList<NetworkIndexStatus> networkStatuses;

                                      try
                                      {
                                          status = await _searchService.GetStatusAsync();
                                          settings = await _searchService.GetMachineSettingsAsync();
                                          networkStatuses = _searchService.GetNetworkIndexStatuses();
                                      }

                                      catch
                                      {
                                          status = new UsnIndexer.IndexerStatus { State = "error" };
                                          settings = new MachineSettings();
                                          networkStatuses = Array.Empty<NetworkIndexStatus>();
                                      }

                                      _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                      {
                                          // Update sub-viewmodels

                                          var isServiceReady = status.State != "error";
                                          var isLocalDriveBusy = status.Drives.Any(d => d.State is "indexing" or "pending");
                                          var isNetworkBusy = networkStatuses.Any(s => s.State is "indexing" or "pending");
                                          var isServiceLifecycleBusy = status.State is "indexing" or "loading-cache" or "pending" || status.IsMaintenanceBusy && !isLocalDriveBusy;
                                          var isBusy = isServiceLifecycleBusy || isLocalDriveBusy || isNetworkBusy;
                                          Service.UpdateStatus(status);
                                          LocalDrive.UpdateStatus(status, settings);
                                          NetworkDrive.RefreshNetworkDrives(_userSettings, networkStatuses);
                                          IsServiceReady = isServiceReady;
                                          IsBusy = isBusy;
                                          CanApply = isServiceReady && !isBusy;
                                          _refreshTimer?.Interval = isBusy ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(5);

                                      }));
                                  });

    public async void Apply()
    {
        if (!CanApply)
            return;

        var previousNetworkDrives = _userSettings.NetworkDrives

            .Select(d => new NetworkDriveSetting
            {
                Id = d.Id,
                RefreshMode = d.RefreshMode

            })

            .ToList();
        var previousLocalDrives = (await _searchService.GetMachineSettingsAsync()).LocalDrives.ToList();
        var previousExclusions = SettingsChangeSnapshot.CaptureExclusions(_userSettings);

        var machineSettings = new MachineSettings
        {
            LocalDrives = LocalDrive.LocalDrives.Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Id)).Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList()

        };
        if (SettingsChangeSnapshot.StringListChanged(previousLocalDrives, machineSettings.LocalDrives))
            await _searchService.SaveMachineSettingsAsync(machineSettings);

        _userSettings.NetworkDrives = NetworkDrive.NetworkDrives.Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Id)).Select(d => new NetworkDriveSetting
        {
            Id = d.Id,
            RefreshMode = d.RefreshMode

        }).ToList();
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
        if (exclusionsChanged)
            _searchService.RefreshNetworkIndexes();
        else if (NetworkSettingsChanged(previousNetworkDrives, _userSettings.NetworkDrives))
            await NetworkDriveApplyHelper.ApplyChangesAsync(_searchService, previousNetworkDrives, _userSettings.NetworkDrives);

        if (exclusionsChanged)
            await RebuildScanBasedLocalDrivesAsync(machineSettings.LocalDrives);

        Refresh();
    }

    private async Task RebuildScanBasedLocalDrivesAsync(IReadOnlyList<string> enabledLocalDriveIds)
    {
        var enabled = enabledLocalDriveIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in LocalDrive.LocalDrives.Where(d => d.IsEnabled && (enabled.Count == 0 || enabled.Contains(d.Id))))
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
}
