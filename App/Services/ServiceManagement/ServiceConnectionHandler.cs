using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SwiftList.Core;
using SwiftList.Core.Indexer.Usn;
using Application = System.Windows.Application;
namespace SwiftList.App.Services
{
    public class ServiceConnectionHandler : IDisposable
    {
        private static readonly TimeSpan ServiceReconnectGracePeriod = TimeSpan.FromSeconds(15);
        private readonly SearchService _searchService;
        private readonly DispatcherTimer _statusTimer;
        private readonly Action<UsnIndexer.IndexerStatus> _onStatusUpdated;
        private readonly Action _onServiceInstallStarted;
        private readonly Action _onServiceInstallCompleted;
        private readonly Action<Exception> _onServiceInstallError;
        private readonly Action _onServiceFailedToStart;
        private bool _hasAttemptedAutoInstall;
        private bool _isAutoInstallingService;
        private DateTime _serviceReconnectUntilUtc = DateTime.MinValue;
        private int _isStatusCheckInFlight;
        public bool IsAutoInstallingService => _isAutoInstallingService;
        public bool HasAttemptedAutoInstall => _hasAttemptedAutoInstall;

        public ServiceConnectionHandler(

            SearchService searchService,
            Action<UsnIndexer.IndexerStatus> onStatusUpdated,
            Action onServiceInstallStarted,
            Action onServiceInstallCompleted,
            Action<Exception> onServiceInstallError,
            Action onServiceFailedToStart,
            int pollIntervalMs = 400)
        {
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
            _onStatusUpdated = onStatusUpdated ?? throw new ArgumentNullException(nameof(onStatusUpdated));
            _onServiceInstallStarted = onServiceInstallStarted ?? throw new ArgumentNullException(nameof(onServiceInstallStarted));
            _onServiceInstallCompleted = onServiceInstallCompleted ?? throw new ArgumentNullException(nameof(onServiceInstallCompleted));
            _onServiceInstallError = onServiceInstallError ?? throw new ArgumentNullException(nameof(onServiceInstallError));
            _onServiceFailedToStart = onServiceFailedToStart ?? throw new ArgumentNullException(nameof(onServiceFailedToStart));
            _statusTimer = new DispatcherTimer();
            _statusTimer.Interval = TimeSpan.FromMilliseconds(pollIntervalMs);
            _statusTimer.Tick += (s, e) => PollStatusTick();
        }

        public void Start()
        {
            _statusTimer.Start();
        }

        public void Stop()
        {
            _statusTimer.Stop();
        }

        public void BeginServiceReconnectGracePeriod()
        {
            _serviceReconnectUntilUtc = DateTime.UtcNow.Add(ServiceReconnectGracePeriod);
        }

        public bool ShouldWaitForServiceReconnect()
        {
            return _isAutoInstallingService || DateTime.UtcNow < _serviceReconnectUntilUtc;
        }

        public void ClearServiceReconnectState()
        {
            _hasAttemptedAutoInstall = false;
            _isAutoInstallingService = false;
            _serviceReconnectUntilUtc = DateTime.MinValue;
        }

        public void ResetAutoInstallFlag()
        {
            _hasAttemptedAutoInstall = false;
        }

        public void PollStatusTick()
        {
            if (Interlocked.Exchange(ref _isStatusCheckInFlight, 1) == 1)
                return;

            Task.Run(async () =>
            {
                var status = await _searchService.GetStatusAsync();

                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        ProcessStatus(status);
                    }

                    finally
                    {
                        Interlocked.Exchange(ref _isStatusCheckInFlight, 0);
                    }

                }));
            });
        }

        private void ProcessStatus(UsnIndexer.IndexerStatus status)
        {
            if (status.State == "error")
            {
                if (ShouldWaitForServiceReconnect())
                {
                    _onStatusUpdated?.Invoke(new UsnIndexer.IndexerStatus { State = "reconnecting" });
                    return;
                }

                _statusTimer.Stop();
                if (!_hasAttemptedAutoInstall)
                {
                    _hasAttemptedAutoInstall = true;
                    AttemptSilentInstall();
                    return;
                }

                _onServiceFailedToStart?.Invoke();
                return;
            }

            _onStatusUpdated?.Invoke(status);
        }

        public void AttemptSilentInstall()
        {
            _isAutoInstallingService = true;
            BeginServiceReconnectGracePeriod();
            _onServiceInstallStarted?.Invoke();

            Task.Run(() =>
            {
                ServiceInstallManager.SilentInstall(() =>
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isAutoInstallingService = false;
                        BeginServiceReconnectGracePeriod();
                        _onServiceInstallCompleted?.Invoke();
                    }));
                });
            });
        }

        public void ExecuteInstallService()
        {
            ServiceInstallManager.InstallService(

                onCompleted: () =>
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _hasAttemptedAutoInstall = true;
                        _isAutoInstallingService = false;
                        BeginServiceReconnectGracePeriod();
                        _onServiceInstallCompleted?.Invoke();
                    }));

                },

                onError: ex =>
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _onServiceInstallError?.Invoke(ex);
                    }));
                }

            );
        }

        public void Dispose()
        {
            _statusTimer.Stop();
        }
    }
}
