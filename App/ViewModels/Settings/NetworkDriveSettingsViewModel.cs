using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.Core;
using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.App.ViewModels;
using SwiftList.App.Services;
using SwiftList.PluginSdk;

namespace SwiftList.App.ViewModels.Settings
{
    public class NetworkDriveSettingsViewModel : ViewModelBase
    {
        private readonly SearchService _searchService;
        private readonly UserSettings _userSettings;
        private readonly Action _onTriggerFastRefresh;
        private string _indexSummary = TranslationManager.Instance["Network_SummaryBusy"];
        private bool _canRebuild;
        private bool _isNetworkDrivesEmpty;
        private string _drivesPlaceholderText = string.Empty;
        private bool _hasPendingEdits;
        private bool _canEditRefreshModes = true;

        public NetworkDriveSettingsViewModel(SearchService searchService, UserSettings userSettings, Action onTriggerFastRefresh)
        {
            _searchService = searchService;
            _userSettings = userSettings;
            _onTriggerFastRefresh = onTriggerFastRefresh;
            RebuildCommand = new RelayCommand(Rebuild, () => CanRebuild);

            // Dynamically refresh properties and child items when the language changes
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

        public void RefreshNetworkDrives(UserSettings userSettings, IReadOnlyList<NetworkIndexStatus>? indexStatuses = null)
        {
            var configured = userSettings.NetworkDrives.ToDictionary(d => d.Drive, StringComparer.OrdinalIgnoreCase);
            var statuses = (indexStatuses ?? Array.Empty<NetworkIndexStatus>())
                .ToDictionary(s => s.Drive, StringComparer.OrdinalIgnoreCase);

            var drives = NetworkDriveResolver.GetNetworkDrives();

            if (HasPendingEdits)
            {
                foreach (var drive in drives)
                {
                    string letter = drive.Letter;
                    var item = NetworkDrives.FirstOrDefault(d => d.Drive.Equals(letter, StringComparison.OrdinalIgnoreCase));
                    if (item == null)
                        continue;

                    statuses.TryGetValue(letter, out var indexStatus);
                    item.State = GetStateText(drive, indexStatus);
                    item.ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-";
                }
            }
            else
            {
                foreach (var existing in NetworkDrives)
                    existing.PropertyChanged -= OnNetworkDriveItemChanged;
                NetworkDrives.Clear();

                foreach (var drive in drives)
                {
                    string letter = drive.Letter;
                    configured.TryGetValue(letter, out var saved);
                    statuses.TryGetValue(letter, out var indexStatus);

                    var item = new NetworkDriveSettingsItem
                    {
                        Drive = letter,
                        IsEnabled = saved?.Enabled ?? false,
                        State = GetStateText(drive, indexStatus),
                        ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-",
                        RefreshMode = NormalizeRefreshMode(saved?.RefreshMode)
                    };
                    item.PropertyChanged += OnNetworkDriveItemChanged;
                    NetworkDrives.Add(item);
                }
            }

            IsNetworkDrivesEmpty = NetworkDrives.Count == 0;
            DrivesPlaceholderText = TranslationManager.Instance["Network_Placeholder"];

            bool hasEnabled = NetworkDrives.Any(d => d.IsEnabled);
            bool isBusy = indexStatuses?.Any(s => s.State == "indexing" || s.State == "pending") == true;
            CanRebuild = hasEnabled && !isBusy;
            CanEditRefreshModes = !isBusy;

            if (IsNetworkDrivesEmpty)
            {
                IndexSummary = TranslationManager.Instance["Network_DrivesEmpty"];
            }
            else
            {
                int enabledCount = NetworkDrives.Count(d => d.IsEnabled);
                int totalItems = (indexStatuses ?? Array.Empty<NetworkIndexStatus>()).Sum(s => s.Items);
                string state = isBusy ? TranslationManager.Instance["Network_StatusIndexing"] : TranslationManager.Instance["Status_Ready"];
                IndexSummary = string.Format(TranslationManager.Instance["Network_SummaryTemplate"], state, enabledCount, totalItems);
            }
        }

        private void Rebuild()
        {
            if (!CanRebuild)
                return;

            _userSettings.NetworkDrives = NetworkDrives.Select(d => new NetworkDriveSetting
            {
                Drive = d.Drive,
                Enabled = d.IsEnabled,
                RefreshMode = d.RefreshMode
            }).ToList();
            _userSettings.Save();
            ResetPendingEdits();

            CanRebuild = false;
            IndexSummary = TranslationManager.Instance["Network_Rebuilding"];
            _searchService.RefreshNetworkIndexes();
            _onTriggerFastRefresh?.Invoke();
        }

        public void ResetPendingEdits()
        {
            HasPendingEdits = false;
        }

        private void OnNetworkDriveItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(NetworkDriveSettingsItem.IsEnabled) or nameof(NetworkDriveSettingsItem.RefreshMode))
                HasPendingEdits = true;
        }

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

        private static string NormalizeRefreshMode(string? refreshMode)
        {
            return refreshMode switch
            {
                "15Minutes" => "15Minutes",
                "Hourly" => "Hourly",
                "Daily" => "Daily",
                _ => "Manual"
            };
        }

    }

    public class NetworkDriveSettingsItem : ViewModelBase
    {
        private bool _isEnabled;
        private string _refreshMode = "Manual";
        private string _state = string.Empty;
        private string _itemCount = string.Empty;

        public string Drive { get; set; } = string.Empty;

        public string State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }

        public string ItemCount
        {
            get => _itemCount;
            set => SetProperty(ref _itemCount, value);
        }

        public string RefreshMode
        {
            get => _refreshMode;
            set
            {
                if (SetProperty(ref _refreshMode, value))
                    OnPropertyChanged(nameof(RefreshModeText));
            }
        }

        public string RefreshModeText => RefreshMode switch
        {
            "Manual" => TranslationManager.Instance["Network_ModeManual"],
            "15Minutes" => TranslationManager.Instance["Network_Mode15M"],
            "Hourly" => TranslationManager.Instance["Network_ModeHourly"],
            "Daily" => TranslationManager.Instance["Network_ModeDaily"],
            _ => RefreshMode
        };

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public void NotifyLanguageChanged()
        {
            OnPropertyChanged(nameof(RefreshModeText));
        }
    }

    public sealed record RefreshModeOption(string Value, string Label)
    {
        public override string ToString() => Label;
    }
}
