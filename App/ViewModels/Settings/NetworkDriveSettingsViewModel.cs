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
    public NetworkDriveSettingsViewModel(SearchService searchService, UserSettings userSettings, Action onTriggerFastRefresh)
    {
        _searchService = searchService;
        _userSettings = userSettings;
        _onTriggerFastRefresh = onTriggerFastRefresh;
        RebuildCommand = new RelayCommand(Rebuild, () => CanRebuild);

        TranslationManager.Instance.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(RefreshModeOptions));
            foreach (var item in NetworkDrives)
            {
                item.NotifyLanguageChanged();
            }
        };
    }

    public ObservableCollection<NetworkDriveSettingsItem> NetworkDrives { get; } = new();
    public bool HasPendingEdits
    {
        get => _hasPendingEdits;
        private set => SetProperty(ref _hasPendingEdits, value);
    }

    public bool CanEditRefreshModes
    {
        get => _canEditRefreshModes;
        private set => SetProperty(ref _canEditRefreshModes, value);
    }

    public IReadOnlyList<RefreshModeOption> RefreshModeOptions => new[]
    {
        new RefreshModeOption("Manual", TranslationManager.Instance["Network_ModeManual"]),
        new RefreshModeOption("15Minutes", TranslationManager.Instance["Network_Mode15M"]),
        new RefreshModeOption("Hourly", TranslationManager.Instance["Network_ModeHourly"]),
        new RefreshModeOption("Daily", TranslationManager.Instance["Network_ModeDaily"])
    };

    public ICommand RebuildCommand { get; }
    public string IndexSummary
    {
        get => _indexSummary;
        set => SetProperty(ref _indexSummary, value);
    }

    public bool CanRebuild
    {
        get => _canRebuild;
        set
        {
            if (SetProperty(ref _canRebuild, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsNetworkDrivesEmpty
    {
        get => _isNetworkDrivesEmpty;
        set => SetProperty(ref _isNetworkDrivesEmpty, value);
    }

    public string DrivesPlaceholderText
    {
        get => _drivesPlaceholderText;
        set => SetProperty(ref _drivesPlaceholderText, value);
    }

    public void RefreshNetworkDrives(UserSettings userSettings, IReadOnlyList<NetworkIndexStatus>? indexStatuses = null, bool isGlobalBusy = false)
    {
        var configured = userSettings.NetworkDrives
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var statuses = (indexStatuses ?? Array.Empty<NetworkIndexStatus>())
            .ToDictionary(s => s.Drive, StringComparer.OrdinalIgnoreCase);

        var resolvedDrives = NetworkDriveResolver.GetNetworkDrives();
        var resolvedByDrive = resolvedDrives.ToDictionary(d => d.Letter, StringComparer.OrdinalIgnoreCase);
        var visibleDrives = resolvedDrives.Select(d => d.Letter)
            .Concat(_searchService.GetCachedNetworkDrives())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (HasPendingEdits)
        {
            foreach (var letter in visibleDrives)
            {
                var item = NetworkDrives.FirstOrDefault(d => d.Drive.Equals(letter, StringComparison.OrdinalIgnoreCase));
                if (item == null)
                    continue;

                statuses.TryGetValue(letter, out var indexStatus);
                resolvedByDrive.TryGetValue(letter, out var drive);
                item.Id = NetworkDriveResolver.GetNetworkId(letter);
                item.IsPresent = drive != null;
                if (!item.IsPresent)
                    item.IsEnabled = false;
                TrackPendingRebuild(letter, indexStatus?.State);

                item.State = drive == null ? TranslationManager.Instance["Network_StatusUnavailable"] : GetStateText(drive, indexStatus);
                item.ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-";
                UpdateRowAction(item);
            }
        }
        else
        {
            foreach (var existing in NetworkDrives)
                existing.PropertyChanged -= OnNetworkDriveItemChanged;
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
                    AppliedEnabled = drive != null && saved != null,
                    State = drive == null ? TranslationManager.Instance["Network_StatusUnavailable"] : GetStateText(drive, indexStatus),
                    ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-",
                    RefreshMode = NormalizeRefreshMode(saved?.RefreshMode)
                };
                item.RowActionCommand = new RelayCommand(() => RunDriveAction(item), () => item.CanRunRowAction);
                TrackPendingRebuild(letter, indexStatus?.State);

                UpdateRowAction(item);
                item.PropertyChanged += OnNetworkDriveItemChanged;
                NetworkDrives.Add(item);
            }
        }

        IsNetworkDrivesEmpty = NetworkDrives.Count == 0;
        DrivesPlaceholderText = TranslationManager.Instance["Network_Placeholder"];

        var hasEnabled = NetworkDrives.Any(d => d.AppliedEnabled);
        var isBusy = isGlobalBusy || _pendingRowRebuilds.Count > 0 || indexStatuses?.Any(s => s.State == "indexing" || s.State == "pending") == true;
        _isBusy = isBusy;
        CanRebuild = hasEnabled && !isBusy;
        CanEditRefreshModes = !isBusy;
        foreach (var drive in NetworkDrives)
        {
            drive.CanEditEnabled = drive.IsPresent && !isBusy;
            drive.CanEditRefreshMode = drive.IsPresent && !isBusy;
            drive.CanRunRowAction = !isBusy && (drive.RowAction == NetworkDriveRowAction.Delete || CanRebuild && drive.RowAction == NetworkDriveRowAction.Rebuild);
        }

        if (IsNetworkDrivesEmpty)
        {
            IndexSummary = TranslationManager.Instance["Network_DrivesEmpty"];
        }
        else
        {
            var enabledCount = NetworkDrives.Count(d => d.AppliedEnabled);
            var totalItems = (indexStatuses ?? Array.Empty<NetworkIndexStatus>()).Sum(s => s.Items);
            var state = isBusy ? TranslationManager.Instance["Network_StatusIndexing"] : TranslationManager.Instance["Status_Ready"];
            IndexSummary = string.Format(TranslationManager.Instance["Network_SummaryTemplate"], state, enabledCount, totalItems);
        }
    }

    private void Rebuild()
    {
        if (!CanRebuild)
            return;
        _isBusy = true;

        _userSettings.NetworkDrives = NetworkDrives.Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Id)).Select(d => new NetworkDriveSetting
        {
            Id = d.Id,
            RefreshMode = d.RefreshMode
        }).ToList();
        _userSettings.Save();
        ResetPendingEdits();

        CanRebuild = false;
        IndexSummary = TranslationManager.Instance["Network_Rebuilding"];
        _searchService.RefreshNetworkIndexes();
        _onTriggerFastRefresh?.Invoke();
    }

    private void RunDriveAction(NetworkDriveSettingsItem item)
    {
        if (_isBusy || !item.CanRunRowAction)
            return;

        item.CanRunRowAction = false;
        if (item.RowAction == NetworkDriveRowAction.Rebuild)
        {
            _isBusy = true;
            _userSettings.NetworkDrives = NetworkDrives.Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Id)).Select(d => new NetworkDriveSetting
            {
                Id = d.Id,
                RefreshMode = d.RefreshMode
            }).ToList();
            _userSettings.Save();
            _searchService.ConfigureNetworkIndexes();
            ResetPendingEdits();
            item.State = TranslationManager.Instance["Network_StatusIndexing"];
            item.ItemCount = "-";
            IndexSummary = TranslationManager.Instance["Network_Rebuilding"];
            _pendingRowRebuilds.Add(item.Drive);
            if (!_searchService.RefreshNetworkDriveIndex(item.Drive))
            {
                _pendingRowRebuilds.Remove(item.Drive);
                _observedRowRebuilds.Remove(item.Drive);
            }
        }
        else if (item.RowAction == NetworkDriveRowAction.Delete)
        {
            _searchService.DeleteNetworkDriveCache(item.Drive);
            item.RowAction = NetworkDriveRowAction.None;
            item.State = item.IsPresent ? TranslationManager.Instance["Network_StatusConnected"] : TranslationManager.Instance["Network_StatusUnavailable"];
            item.ItemCount = "-";
            item.CanRunRowAction = false;
            item.CanEditEnabled = item.IsPresent && !_isBusy;
            item.CanEditRefreshMode = item.IsPresent && !_isBusy;
            if (!item.IsPresent)
                item.IsEnabled = false;
        }
        _onTriggerFastRefresh?.Invoke();
    }

    public void ResetPendingEdits() => HasPendingEdits = false;

    private void OnNetworkDriveItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetworkDriveSettingsItem.IsEnabled) or nameof(NetworkDriveSettingsItem.RefreshMode))
        {
            HasPendingEdits = true;
        }
    }
    private void UpdateRowAction(NetworkDriveSettingsItem item) => item.RowAction = item.AppliedEnabled
            ? NetworkDriveRowAction.Rebuild
            : _searchService.HasNetworkDriveCache(item.Drive) ? NetworkDriveRowAction.Delete : NetworkDriveRowAction.None;

    private static string GetStateText(ResolvedNetworkDrive drive, NetworkIndexStatus? indexStatus)
    {
        if (!drive.IsReady)
            return TranslationManager.Instance["Network_StatusUnavailable"];

        return indexStatus?.State switch
        {
            "indexing" => TranslationManager.Instance["Network_StatusIndexing"],
            "ready" => TranslationManager.Instance["Network_StatusReady"],
            "cached" => TranslationManager.Instance["Network_StatusCached"],
            "error" => TranslationManager.Instance["Network_StatusError"],
            "pending" => TranslationManager.Instance["Network_StatusPending"],
            _ => TranslationManager.Instance["Network_StatusConnected"]
        };
    }

    private static string NormalizeRefreshMode(string? refreshMode) => refreshMode switch
    {
        "15Minutes" => "15Minutes",
        "Hourly" => "Hourly",
        "Daily" => "Daily",
        _ => "Manual"
    };

    private void TrackPendingRebuild(string drive, string? state)
    {
        if (!_pendingRowRebuilds.Contains(drive))
            return;

        if (state == "indexing")
        {
            _observedRowRebuilds.Add(drive);
        }
        else if (_observedRowRebuilds.Contains(drive))
        {
            _pendingRowRebuilds.Remove(drive);
            _observedRowRebuilds.Remove(drive);
        }
    }
}

public sealed record RefreshModeOption(string Value, string Label)
{
    public override string ToString() => Label;
}
