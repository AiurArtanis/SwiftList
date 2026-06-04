using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.PluginSdk;
using SwiftList.Core;
using SwiftList.Core.Indexer.Usn;
using SwiftList.App.ViewModels;

namespace SwiftList.App.ViewModels.Settings
{
    public class LocalDriveSettingsViewModel : ViewModelBase
    {
        private readonly SearchService _searchService;
        private readonly Action _onTriggerFastRefresh;

        private string _indexSummary = TranslationManager.Instance["Local_LoadingInfo"];
        private bool _canRebuild;
        private bool _isLocalDrivesEmpty = false;
        private string _drivesPlaceholderText = "";

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

            LocalDrives.Clear();
            foreach (var drive in status.Drives.OrderBy(d => d.Drive))
            {
                bool isEnabled = enabled == null ? drive.Enabled : enabled.Contains(drive.Drive);
                LocalDrives.Add(new LocalDriveSettingsItem
                {
                    Drive = drive.Drive,
                    Name = $"{drive.Drive}:",
                    IsEnabled = isEnabled,
                    Kind = drive.Kind == "LocalNtfs" ? TranslationManager.Instance["Local_KindLocalNtfs"] : drive.Kind,
                    Strategy = isEnabled ? TranslationManager.Instance["Local_StrategyMftUsn"] : TranslationManager.Instance["Local_StrategyDisabled"],
                    State = TranslateState(drive.State),
                    ItemCount = isEnabled ? $"{drive.Files + drive.Dirs:N0}" : "-"
                });
            }

            IsLocalDrivesEmpty = LocalDrives.Count == 0;

            bool isServiceReady = status.State != "error";
            bool isBusy = status.State is "indexing" or "loading-cache" or "pending";

            CanRebuild = IsUserAdmin && isServiceReady && (status.State is "ready" or "idle");
            IsDriveCheckboxEnabled = IsUserAdmin && isServiceReady && !isBusy;

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
            else
            {
                IndexSummary = string.Format(TranslationManager.Instance["Local_SummaryTemplate"], TranslateState(status.State), LocalDrives.Count(d => d.IsEnabled), status.TotalFiles + status.TotalDirs);
            }
        }

        private void Rebuild()
        {
            if (!CanRebuild)
                return;

            CanRebuild = false;
            IsLocalDrivesEmpty = false;
            IndexSummary = TranslationManager.Instance["Local_Rebuilding"];

            _searchService.InitializeOrLoadIndex(true);
            _onTriggerFastRefresh?.Invoke();
        }

        private static string TranslateState(string state)
        {
            return state switch
            {
                "ready" => TranslationManager.Instance["Local_StateReady"],
                "indexing" => TranslationManager.Instance["Local_StateIndexing"],
                "loading-cache" => TranslationManager.Instance["Local_StateLoadingCache"],
                "pending" => TranslationManager.Instance["Local_StatePending"],
                "disabled" => TranslationManager.Instance["Local_StateDisabled"],
                "failed" => TranslationManager.Instance["Local_StateFailed"],
                "error" => TranslationManager.Instance["Local_StateError"],
                "idle" => TranslationManager.Instance["Local_StateIdle"],
                _ => state
            };
        }
    }

    public class LocalDriveSettingsItem : ViewModelBase
    {
        private bool _isEnabled;
        public string Drive { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Strategy { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ItemCount { get; set; } = string.Empty;
        public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    }
}
