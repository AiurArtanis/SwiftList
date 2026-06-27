using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;
using SwiftList.Core.Indexer.Usn;
namespace SwiftList.App.ViewModels.Settings;

public class LocalDriveSettingsViewModel : ViewModelBase
{
    private readonly SearchService _searchService;
    private readonly Action _onTriggerFastRefresh;
    private readonly HashSet<string> _pendingRowRebuilds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _observedRowRebuilds = new(StringComparer.OrdinalIgnoreCase);
    private string _indexSummary = TranslationManager.Instance["Local_LoadingInfo"];
    private bool _canRebuild;
    private bool _isLocalDrivesEmpty = false;
    private string _drivesPlaceholderText = "";
    private bool _isBusy;

    public LocalDriveSettingsViewModel(SearchService searchService, Action onTriggerFastRefresh)
    {
        _searchService = searchService;
        _onTriggerFastRefresh = onTriggerFastRefresh;
        RebuildCommand = new RelayCommand(Rebuild, () => CanRebuild);
    }

    public ObservableCollection<LocalDriveSettingsItem> LocalDrives { get; } = new();
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

    public bool IsLocalDrivesEmpty
    {
        get => _isLocalDrivesEmpty;
        set => SetProperty(ref _isLocalDrivesEmpty, value);
    }

    public string DrivesPlaceholderText
    {
        get => _drivesPlaceholderText;
        set => SetProperty(ref _drivesPlaceholderText, value);
    }

    public bool IsUserAdmin => UpdateService.Instance.IsUserAdmin();
    private bool _isDriveCheckboxEnabled;

    public bool IsDriveCheckboxEnabled
    {
        get => _isDriveCheckboxEnabled;
        set => SetProperty(ref _isDriveCheckboxEnabled, value);
    }

    public void UpdateStatus(UsnIndexer.IndexerStatus status, MachineSettings settings)
    {
        var enabled = settings.EnabledLocalDrives.Count == 0

            ? null

            : settings.EnabledLocalDrives.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in status.Drives.OrderBy(d => d.Drive))
        {
            var item = LocalDrives.FirstOrDefault(d => d.Drive.Equals(drive.Drive, StringComparison.OrdinalIgnoreCase));
            var isPresent = drive.State != "unavailable";
            var appliedEnabled = isPresent && (enabled == null ? drive.Enabled : enabled.Contains(drive.Drive));
            var isEnabled = item?.IsEnabled ?? appliedEnabled;
            if (item == null)
            {
                item = new LocalDriveSettingsItem
                {
                    Drive = drive.Drive,
                    Name = $"{drive.Drive}:",
                    IsEnabled = isEnabled
                };
                item.RowActionCommand = new RelayCommand(() => RunDriveAction(item), () => item.CanRunRowAction);
                item.PropertyChanged += OnLocalDriveItemChanged;
                LocalDrives.Add(item);
            }

            var hasCache = FileRecordStoreSerializer.ExistsBasePath(FileRecordStoreSerializer.GetBasePath(Path.GetDirectoryName(drive.CachePath) ?? string.Empty, drive.Drive));
            TrackPendingRebuild(drive);

            item.RowAction = appliedEnabled ? LocalDriveRowAction.Rebuild : hasCache ? LocalDriveRowAction.Delete : LocalDriveRowAction.None;
            item.CanRunRowAction = _pendingRowRebuilds.Count == 0 && (item.RowAction == LocalDriveRowAction.Delete || CanRebuild && item.RowAction == LocalDriveRowAction.Rebuild);
            item.CanEditEnabled = isPresent && IsDriveCheckboxEnabled;
            item.CachePath = drive.CachePath;
            item.Kind = drive.Kind == "LocalNtfs" ? TranslationManager.Instance["Local_KindLocalNtfs"] : drive.Kind;
            item.Strategy = appliedEnabled ? TranslationManager.Instance["Local_StrategyMftUsn"] : TranslationManager.Instance["Local_StrategyDisabled"];
            item.State = TranslateState(drive.State);
            item.ItemCount = appliedEnabled && drive.Files + drive.Dirs > 0 ? $"{drive.Files + drive.Dirs:N0}" : "-";
        }

        for (var i = LocalDrives.Count - 1; i >= 0; i--)
        {
            if (!status.Drives.Any(d => d.Drive.Equals(LocalDrives[i].Drive, StringComparison.OrdinalIgnoreCase)))
            {
                LocalDrives[i].PropertyChanged -= OnLocalDriveItemChanged;
                LocalDrives.RemoveAt(i);
            }
        }

        IsLocalDrivesEmpty = LocalDrives.Count == 0;
        var isServiceReady = status.State != "error";
        var hasPendingRebuild = _pendingRowRebuilds.Count > 0;
        var hasBusyDrive = status.Drives.Any(d => d.State is "indexing" or "pending");
        var isBusy = status.IsMaintenanceBusy || status.State is "indexing" or "loading-cache" or "pending" || hasPendingRebuild || hasBusyDrive;
        _isBusy = isBusy;
        CanRebuild = IsUserAdmin && isServiceReady && (status.State is "ready" or "idle") && !status.IsMaintenanceBusy && !hasPendingRebuild && !hasBusyDrive;
        IsDriveCheckboxEnabled = IsUserAdmin && isServiceReady && !isBusy;
        foreach (var drive in LocalDrives)
        {
            drive.CanRunRowAction = !isBusy && (drive.RowAction == LocalDriveRowAction.Delete || CanRebuild && drive.RowAction == LocalDriveRowAction.Rebuild);
            drive.CanEditEnabled = drive.State != TranslationManager.Instance["Local_DriveUnavailable"] && IsDriveCheckboxEnabled;
        }
        if (!isServiceReady)
        {
            IndexSummary = TranslationManager.Instance["Local_ErrorDisconnected"];
            DrivesPlaceholderText = TranslationManager.Instance["Local_ErrorPlaceholder"];
        }

        else if (LocalDrives.Count == 0)
        {
            IndexSummary = TranslationManager.Instance["Local_LoadingInfo"];
            DrivesPlaceholderText = TranslationManager.Instance["Local_LoadingPlaceholder"];
        }

        else if (isBusy)
            IndexSummary = TranslationManager.Instance["Local_Rebuilding"];
        else
            IndexSummary = string.Format(TranslationManager.Instance["Local_SummaryTemplate"], TranslateState(status.State), status.Drives.Count(d => d.Enabled), status.TotalFiles + status.TotalDirs);
    }

    private async void Rebuild()
    {
        if (!CanRebuild)
            return;
        SetBusy(true);
        IsLocalDrivesEmpty = false;
        IndexSummary = TranslationManager.Instance["Local_Rebuilding"];
        await _searchService.InitializeOrLoadIndexAsync(true);
        _onTriggerFastRefresh?.Invoke();
    }

    private void OnLocalDriveItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LocalDriveSettingsItem.IsEnabled) || sender is not LocalDriveSettingsItem item)
            return;

        item.CanRunRowAction = !_isBusy && item.CanRunRowAction;
    }

    private async void RunDriveAction(LocalDriveSettingsItem item)
    {
        if (_isBusy || !item.CanRunRowAction)
            return;

        if (item.RowAction == LocalDriveRowAction.Rebuild)
        {
            SetBusy(true);
            item.State = TranslationManager.Instance["Local_StateIndexing"];
            item.ItemCount = "-";
            IndexSummary = TranslationManager.Instance["Local_Rebuilding"];
            _pendingRowRebuilds.Add(item.Drive);
            _onTriggerFastRefresh?.Invoke();
            if (!await _searchService.RebuildDriveIndexAsync(item.Drive))
            {
                _pendingRowRebuilds.Remove(item.Drive);
                _observedRowRebuilds.Remove(item.Drive);
            }
        }
        else if (item.RowAction == LocalDriveRowAction.Delete)
        {
            await _searchService.DeleteDriveIndexAsync(item.Drive);
            item.RowAction = LocalDriveRowAction.None;
            item.State = TranslationManager.Instance["Local_StateDisabled"];
            item.ItemCount = "-";
        }

        _onTriggerFastRefresh?.Invoke();
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        CanRebuild = !isBusy && CanRebuild;
        IsDriveCheckboxEnabled = !isBusy && IsDriveCheckboxEnabled;
        foreach (var drive in LocalDrives)
            (drive.CanRunRowAction, drive.CanEditEnabled) = (!isBusy && drive.CanRunRowAction, !isBusy && drive.CanEditEnabled);
    }

    private static string TranslateState(string state) => state switch
    {
        "ready" => TranslationManager.Instance["Local_StateReady"],
        "indexing" => TranslationManager.Instance["Local_StateIndexing"],
        "loading-cache" => TranslationManager.Instance["Local_StateLoadingCache"],
        "pending" => TranslationManager.Instance["Local_StatePending"],
        "disabled" => TranslationManager.Instance["Local_StateDisabled"],
        "unavailable" => TranslationManager.Instance["Local_DriveUnavailable"],
        "failed" => TranslationManager.Instance["Local_StateFailed"],
        "error" => TranslationManager.Instance["Local_StateError"],
        "idle" => TranslationManager.Instance["Local_StateIdle"],
        _ => state

    };

    private void TrackPendingRebuild(UsnIndexer.DriveIndexStatus drive)
    {
        if (!_pendingRowRebuilds.Contains(drive.Drive))
            return;

        if (drive.State == "indexing")
        {
            _observedRowRebuilds.Add(drive.Drive);
        }
        else if (drive.State is "ready" or "failed" or "disabled" or "unavailable")
        {
            _pendingRowRebuilds.Remove(drive.Drive);
            _observedRowRebuilds.Remove(drive.Drive);
        }
        else if (_observedRowRebuilds.Contains(drive.Drive))
        {
            _pendingRowRebuilds.Remove(drive.Drive);
            _observedRowRebuilds.Remove(drive.Drive);
        }
    }
}

public class LocalDriveSettingsItem : ViewModelBase
{
    private bool _isEnabled;
    private string _kind = string.Empty;
    private string _strategy = string.Empty;
    private string _state = string.Empty;
    private string _itemCount = string.Empty;
    private bool _canRunRowAction;
    private bool _canEditEnabled;
    private LocalDriveRowAction _rowAction;
    public string Drive { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CachePath { get; set; } = string.Empty;
    public ICommand RowActionCommand { get; set; } = null!;

    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    public string Kind { get => _kind; set => SetProperty(ref _kind, value); }
    public string Strategy { get => _strategy; set => SetProperty(ref _strategy, value); }
    public string State { get => _state; set => SetProperty(ref _state, value); }
    public string ItemCount { get => _itemCount; set => SetProperty(ref _itemCount, value); }
    public bool CanEditEnabled { get => _canEditEnabled; set => SetProperty(ref _canEditEnabled, value); }
    public bool CanRunRowAction
    {
        get => _canRunRowAction;
        set
        {
            if (SetProperty(ref _canRunRowAction, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }
    public LocalDriveRowAction RowAction
    {
        get => _rowAction;
        set
        {
            if (!SetProperty(ref _rowAction, value))
                return;
            OnPropertyChanged(nameof(IsRowActionVisible));
            OnPropertyChanged(nameof(RowActionText));
        }
    }
    public bool IsRowActionVisible => RowAction != LocalDriveRowAction.None;
    public string RowActionText => RowAction switch
    {
        LocalDriveRowAction.Rebuild => TranslationManager.Instance["Local_RowRebuildBtn"],
        LocalDriveRowAction.Delete => TranslationManager.Instance["Local_RowDeleteBtn"],
        _ => string.Empty
    };
}

public enum LocalDriveRowAction
{
    None,
    Rebuild,
    Delete
}
