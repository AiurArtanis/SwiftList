using System.Windows.Threading;
using SwiftList.Core;
using SwiftList.Core.Indexer.Usn;
using Application = System.Windows.Application;

namespace SwiftList.App.Services;

public class ServiceConnectionHandler : IDisposable
{
    private static readonly TimeSpan ServiceReconnectGracePeriod = TimeSpan.FromSeconds(15);
    private static readonly object GlobalMonitorLock = new();
    private static readonly List<ServiceConnectionHandler> ActiveSubscribers = new();
    private static DispatcherTimer? _sharedStatusTimer;
    private static SearchService? _sharedSearchService;
    private static int _isStatusCheckInFlight;
    private static bool _globalAutoInstallingService;
    private static bool _globalAutoInstallAttempted;
    private static DateTime _globalReconnectUntilUtc = DateTime.MinValue;

    private readonly SearchService _searchService;
    private readonly Action<UsnIndexer.IndexerStatus> _onStatusUpdated;
    private readonly Action _onServiceInstallStarted;
    private readonly Action _onServiceInstallCompleted;
    private readonly Action<Exception> _onServiceInstallError;
    private readonly Action _onServiceFailedToStart;
    private readonly Action _onServiceReachable;
    private readonly int _pollIntervalMs;
    private bool _isMonitoringActive;
    private bool _needsDetailedStatus;
    private bool _reachableCallbackIssued;

    public bool IsAutoInstallingService => _globalAutoInstallingService;
    public bool HasAttemptedAutoInstall => _globalAutoInstallAttempted;

    public ServiceConnectionHandler(
        SearchService searchService,
        Action<UsnIndexer.IndexerStatus> onStatusUpdated,
        Action onServiceInstallStarted,
        Action onServiceInstallCompleted,
        Action<Exception> onServiceInstallError,
        Action onServiceFailedToStart,
        Action onServiceReachable,
        int pollIntervalMs = 400)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _onStatusUpdated = onStatusUpdated ?? throw new ArgumentNullException(nameof(onStatusUpdated));
        _onServiceInstallStarted = onServiceInstallStarted ?? throw new ArgumentNullException(nameof(onServiceInstallStarted));
        _onServiceInstallCompleted = onServiceInstallCompleted ?? throw new ArgumentNullException(nameof(onServiceInstallCompleted));
        _onServiceInstallError = onServiceInstallError ?? throw new ArgumentNullException(nameof(onServiceInstallError));
        _onServiceFailedToStart = onServiceFailedToStart ?? throw new ArgumentNullException(nameof(onServiceFailedToStart));
        _onServiceReachable = onServiceReachable ?? throw new ArgumentNullException(nameof(onServiceReachable));
        _pollIntervalMs = pollIntervalMs;
    }

    public void Start(bool requireDetailedStatus = false)
    {
        lock (GlobalMonitorLock)
        {
            if (_isMonitoringActive)
            {
                _needsDetailedStatus |= requireDetailedStatus;
                EnsureSharedTimer_NoLock();
                return;
            }

            _isMonitoringActive = true;
            _needsDetailedStatus = requireDetailedStatus;
            _reachableCallbackIssued = false;
            if (!ActiveSubscribers.Contains(this))
                ActiveSubscribers.Add(this);

            EnsureSharedTimer_NoLock();
        }
    }

    public void Stop()
    {
        lock (GlobalMonitorLock)
        {
            if (!_isMonitoringActive)
                return;

            _isMonitoringActive = false;
            _needsDetailedStatus = false;
            _reachableCallbackIssued = false;
            ActiveSubscribers.Remove(this);
            if (ActiveSubscribers.Count == 0)
                StopSharedTimer_NoLock();
        }
    }

    public void BeginServiceReconnectGracePeriod() => _globalReconnectUntilUtc = DateTime.UtcNow.Add(ServiceReconnectGracePeriod);

    public bool ShouldWaitForServiceReconnect() => _globalAutoInstallingService || DateTime.UtcNow < _globalReconnectUntilUtc;

    public void ClearServiceReconnectState()
    {
        _globalAutoInstallAttempted = false;
        _globalAutoInstallingService = false;
        _globalReconnectUntilUtc = DateTime.MinValue;
    }

    public void ResetAutoInstallFlag() => _globalAutoInstallAttempted = false;

    public void AttemptSilentInstall()
    {
        if (_globalAutoInstallingService)
            return;

        // Fast path: if the service is already installed at the current exe path, start it without
        // elevation instead of prompting for a reinstall.
        if (ServiceInstallManager.TryStartExistingService())
        {
            BeginServiceReconnectGracePeriod();
            NotifySubscribers(subscriber => subscriber._onServiceInstallCompleted());
            return;
        }

        _globalAutoInstallingService = true;
        BeginServiceReconnectGracePeriod();
        NotifySubscribers(subscriber => subscriber._onServiceInstallStarted());

        var started = ServiceInstallManager.SilentInstall(() => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            _globalAutoInstallingService = false;
            BeginServiceReconnectGracePeriod();
            NotifySubscribers(subscriber => subscriber._onServiceInstallCompleted());
        })));

        if (!started)
        {
            Logger.Log("[ServiceConnectionHandler] Silent service install already running; waiting for reconnect.", LogLevel.Debug);
            BeginServiceReconnectGracePeriod();
        }
    }

    public void ExecuteInstallService() => ServiceInstallManager.InstallService(
        onCompleted: () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            _globalAutoInstallAttempted = true;
            _globalAutoInstallingService = false;
            BeginServiceReconnectGracePeriod();
            NotifySubscribers(subscriber => subscriber._onServiceInstallCompleted());
        })),
        onError: ex => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            NotifySubscribers(subscriber => subscriber._onServiceInstallError(ex))))
    );

    public void Dispose() => Stop();

    private static void EnsureSharedTimer_NoLock()
    {
        if (_sharedStatusTimer == null)
        {
            _sharedStatusTimer = new DispatcherTimer();
            _sharedStatusTimer.Tick += (_, _) => PollStatusTick();
        }

        if (_sharedSearchService == null && ActiveSubscribers.Count > 0)
            _sharedSearchService = ActiveSubscribers[0]._searchService;

        var interval = ActiveSubscribers.Count > 0
            ? ActiveSubscribers.Min(s => s._pollIntervalMs)
            : 400;
        _sharedStatusTimer.Interval = TimeSpan.FromMilliseconds(interval);
        _sharedStatusTimer.Start();
    }

    private static void StopSharedTimer_NoLock()
    {
        _sharedStatusTimer?.Stop();
        _sharedSearchService = null;
    }

    private static void PollStatusTick()
    {
        if (Interlocked.Exchange(ref _isStatusCheckInFlight, 1) == 1)
            return;

        var searchService = _sharedSearchService;
        if (searchService == null)
        {
            Interlocked.Exchange(ref _isStatusCheckInFlight, 0);
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                if (RequiresDetailedStatus())
                {
                    var status = await searchService.GetStatusAsync().ConfigureAwait(false);
                    _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            NotifySubscribers(subscriber => subscriber.ProcessStatus(status));
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _isStatusCheckInFlight, 0);
                        }
                    }));
                    return;
                }

                var isReachable = await searchService.PingAsync().ConfigureAwait(false);
                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        NotifySubscribers(subscriber => subscriber.ProcessPingResult(isReachable));
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isStatusCheckInFlight, 0);
                    }
                }));
            }
            catch
            {
                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        NotifySubscribers(subscriber => subscriber.ProcessPingResult(false));
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isStatusCheckInFlight, 0);
                    }
                }));
            }
        });
    }

    private static bool RequiresDetailedStatus() { lock (GlobalMonitorLock) return ActiveSubscribers.Any(s => s._needsDetailedStatus); }

    private void ProcessStatus(UsnIndexer.IndexerStatus status)
    {
        if (status.State == "error")
        {
            if (ShouldWaitForServiceReconnect())
            {
                _onStatusUpdated(new UsnIndexer.IndexerStatus { State = "reconnecting" });
                return;
            }

            if (!_globalAutoInstallAttempted)
            {
                _globalAutoInstallAttempted = true;
                AttemptSilentInstall();
                _onStatusUpdated(new UsnIndexer.IndexerStatus { State = "reconnecting" });
                return;
            }

            Stop();
            _onServiceFailedToStart();
            return;
        }

        _onStatusUpdated(status);
    }

    private void ProcessPingResult(bool isReachable)
    {
        if (!isReachable)
        {
            if (ShouldWaitForServiceReconnect())
            {
                _onStatusUpdated(new UsnIndexer.IndexerStatus { State = "reconnecting" });
                return;
            }

            if (!_globalAutoInstallAttempted)
            {
                _globalAutoInstallAttempted = true;
                AttemptSilentInstall();
                _onStatusUpdated(new UsnIndexer.IndexerStatus { State = "reconnecting" });
                return;
            }

            Stop();
            _onServiceFailedToStart();
            return;
        }

        if (_needsDetailedStatus || _reachableCallbackIssued)
            return;

        _reachableCallbackIssued = true;
        _onServiceReachable();
    }

    private static void NotifySubscribers(Action<ServiceConnectionHandler> action)
    {
        ServiceConnectionHandler[] subs;
        lock (GlobalMonitorLock) subs = ActiveSubscribers.ToArray();
        foreach (var sub in subs) action(sub);
    }
}
