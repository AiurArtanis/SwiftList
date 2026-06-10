using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SwiftList.Core;
using SwiftList.Core.Indexer.Usn;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using Application = System.Windows.Application;
using MessageBox = SwiftList.App.Views.Controls.CustomMessageBox;
using SwiftList.App.ViewModels;
using SwiftList.App.ViewModels.Search;
namespace SwiftList.App.ViewModels.Service
{
    public class ServiceMonitorViewModel : ViewModelBase, IDisposable
    {
        private readonly QuickSearchViewModel _mainVm;
        private readonly SearchService _searchService;
        private readonly ServiceConnectionHandler _connectionHandler;
        private readonly ServiceMonitorStatusHandler _statusHandler;
        private bool _isIndexReady;
        private string _statusText = string.Empty;

        // UI Panel Visibilities

        private Visibility _statusBarVisibility = Visibility.Collapsed;
        private Visibility _loadingPanelVisibility = Visibility.Collapsed;
        private Visibility _progressBarVisibility = Visibility.Collapsed;
        private Visibility _installButtonVisibility = Visibility.Collapsed;
        private Visibility _errorIconVisibility = Visibility.Collapsed;

        // Loading/Connection texts

        private string _loadingTitle = string.Empty;
        private string _loadingStats = string.Empty;
        private double _loadingProgress;
        private bool _isProgressIndeterminate = true;

        // Status cache

        private int _statusFiles;
        private int _statusDirs;

        public Visibility ErrorIconVisibility
        {
            get => _errorIconVisibility;
            set => SetProperty(ref _errorIconVisibility, value);
        }

        public ServiceMonitorViewModel(QuickSearchViewModel mainVm, SearchService searchService)
        {
            _mainVm = mainVm;
            _searchService = searchService;

            _connectionHandler = new ServiceConnectionHandler(

                _searchService,
                onStatusUpdated: OnStatusUpdated,
                onServiceInstallStarted: () =>
                {
                    LoadingTitle = TranslationManager.Instance["Service_AutoConnecting"];
                    LoadingStats = TranslationManager.Instance["Service_AdminPrivilegeTip"];
                    ShowServiceReconnectState(LoadingTitle, LoadingStats);

                },

                onServiceInstallCompleted: () =>
                {
                    TriggerIndexBuild();

                },

                onServiceInstallError: ex =>
                {
                    MessageBox.Show(string.Format(TranslationManager.Instance["Service_InstallFailedPrompt"], ex.Message), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);

                },

                onServiceFailedToStart: () =>
                {
                    IsIndexReady = false;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Application.Current.MainWindow?.Hide();
                    });
                    App.ShowSettingsWindow("Service");
                }

            );
            _statusHandler = new ServiceMonitorStatusHandler(this, _mainVm, _connectionHandler);
            InstallServiceCommand = new RelayCommand(_connectionHandler.ExecuteInstallService);
        }

        public ICommand InstallServiceCommand { get; }

        private void OnStatusUpdated(UsnIndexer.IndexerStatus status)

            => _statusHandler.ProcessStatusTimerTick(status);

        public bool IsIndexReady
        {
            get => _isIndexReady;
            set => SetProperty(ref _isIndexReady, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public Visibility StatusBarVisibility
        {
            get => _statusBarVisibility;
            set => SetProperty(ref _statusBarVisibility, value);
        }

        public Visibility LoadingPanelVisibility
        {
            get => _loadingPanelVisibility;
            set => SetProperty(ref _loadingPanelVisibility, value);
        }

        public Visibility ProgressBarVisibility
        {
            get => _progressBarVisibility;
            set => SetProperty(ref _progressBarVisibility, value);
        }

        public Visibility InstallButtonVisibility
        {
            get => _installButtonVisibility;
            set => SetProperty(ref _installButtonVisibility, value);
        }

        public string LoadingTitle
        {
            get => _loadingTitle;
            set => SetProperty(ref _loadingTitle, value);
        }

        public string LoadingStats
        {
            get => _loadingStats;
            set => SetProperty(ref _loadingStats, value);
        }

        public double LoadingProgress
        {
            get => _loadingProgress;
            set => SetProperty(ref _loadingProgress, value);
        }

        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            set => SetProperty(ref _isProgressIndeterminate, value);
        }

        public void TriggerIndexBuild(bool forceRebuild = false)
        {
            if (string.IsNullOrWhiteSpace(_mainVm.Search.SearchQuery))
            {
                _mainVm.Search.ResultsPanelVisibility = Visibility.Visible;
                _mainVm.Search.ResultsSeparatorVisibility = Visibility.Visible;
                if (_connectionHandler.ShouldWaitForServiceReconnect())
                {
                    LoadingPanelVisibility = Visibility.Visible;
                    StatusBarVisibility = Visibility.Collapsed;
                    ProgressBarVisibility = Visibility.Visible;
                    IsProgressIndeterminate = true;
                    InstallButtonVisibility = Visibility.Collapsed;
                    LoadingTitle = TranslationManager.Instance["Service_WaitingStart"];
                    LoadingStats = TranslationManager.Instance["Service_StartedDetail"];
                }

                else
                {
                    LoadingPanelVisibility = Visibility.Collapsed;
                    StatusBarVisibility = Visibility.Visible;
                }
            }

            else
            {
                LoadingPanelVisibility = Visibility.Collapsed;
                StatusBarVisibility = Visibility.Visible;
            }

            _ = Task.Run(async () =>
            {
                UsnIndexer.IndexerStatus status;
                if (!forceRebuild)
                {
                    status = await _searchService.GetStatusAsync();
                }

                else
                {
                    status = new UsnIndexer.IndexerStatus { State = "force-rebuild" };
                }

                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (status.State == "ready")
                    {
                        IsIndexReady = true;
                        _connectionHandler.ClearServiceReconnectState();
                        ApplyReadyStatus(status);
                        LoadingPanelVisibility = Visibility.Collapsed;
                        StatusBarVisibility = Visibility.Visible;
                        _mainVm.Search.PerformSearch(_mainVm.Search.SearchQuery);
                        Logger.Log($"[ServiceMonitor] Connection fast-pass: service already ready.");
                    }

                    else
                    {
                        IsIndexReady = false;
                        if (!_connectionHandler.ShouldWaitForServiceReconnect())
                        {
                            _connectionHandler.ResetAutoInstallFlag();
                        }

                        _statusHandler.ProcessStatusTimerTick(status);

                        Task.Run(async () =>
                        {
                            await _searchService.InitializeOrLoadIndexAsync(forceRebuild);

                            _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                _connectionHandler.Start();
                            }));
                        });
                    }

                }));
            });
        }

        public void StopStatusTimer()
        {
            _connectionHandler.Stop();
        }

        public void ShowServiceReconnectState(string title, string stats)
        {
            IsIndexReady = false;
            ProgressBarVisibility = Visibility.Visible;
            IsProgressIndeterminate = true;
            InstallButtonVisibility = Visibility.Collapsed;
            ErrorIconVisibility = Visibility.Collapsed;
            LoadingPanelVisibility = Visibility.Visible;
            StatusBarVisibility = Visibility.Collapsed;
            LoadingTitle = title;
            LoadingStats = stats;
        }

        public void SetOfflineState()
        {
            IsIndexReady = false;
            ProgressBarVisibility = Visibility.Collapsed;
            InstallButtonVisibility = Visibility.Visible;
            ErrorIconVisibility = Visibility.Visible;
            LoadingPanelVisibility = Visibility.Visible;
            LoadingTitle = TranslationManager.Instance["Service_DisconnectedTitle"];
            LoadingStats = TranslationManager.Instance["Service_DisconnectedDetail"];
            StatusBarVisibility = Visibility.Collapsed;
        }

        // Called by ServiceMonitorStatusHandler when index reaches ready state

        internal void ApplyReadyStatus(UsnIndexer.IndexerStatus status)
        {
            _statusFiles = status.TotalFiles;
            _statusDirs = status.TotalDirs;
            StatusText = string.Format(TranslationManager.Instance["Service_IndexedTemplate"], _statusFiles, _statusDirs);
        }

        public int GetStatusFiles() => _statusFiles;
        public int GetStatusDirs() => _statusDirs;

        public void Dispose()
        {
            _connectionHandler.Dispose();
        }
    }
}
