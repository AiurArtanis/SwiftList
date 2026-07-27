using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.QuickLookBridge;

// Talks directly to QuickLook's (github.com/QL-Win/QuickLook) named pipe -- the exact same one its own
// CLI second-instance forwarding uses -- instead of spawning a process per preview. Pipe name and message
// IDs below are copied verbatim from QuickLook/PipeServerManager.cs; they're QuickLook's private
// implementation detail, not a published API, so a future QuickLook release could change them without
// notice and silently break this.
internal static class QuickLookPipeClient
{
    private const int ConnectTimeoutMs = 1000;

    // QuickLook.App.PipeMessages.Toggle, sent WITH a non-empty options string -- QuickLook's own
    // TogglePreview(path, options) only takes its "hide if already showing this same path" branch when
    // options is empty; a non-empty options string always routes to InvokePreviewWithOption(path,
    // options) instead (see ViewWindowManager.cs), i.e. plain show/update, same as the Invoke message,
    // plus whatever the options ask for. Used (with "top") to keep the docked window topmost so it
    // doesn't get lost behind SwiftList's own window.
    private const string ToggleMessage = "QuickLook.App.PipeMessages.Toggle";
    private const string TopOption = "top";

    // QuickLook.App.PipeMessages.Close -- hides QuickLook's viewer window via its own code path. Used on
    // preview-session teardown so QuickLook's own window-state bookkeeping (_viewerWindow.IsVisible etc.)
    // stays in sync instead of just being abandoned.
    private const string CloseMessage = "QuickLook.App.PipeMessages.Close";

    // Anything QuickLook's own PipeMessages switch doesn't recognize falls into its `default: return
    // false` branch -- a real message ID with zero visible side effect, used purely to test reachability.
    private const string PingMessage = "SwiftList.Plugins.QuickLookBridge.Ping";

    private static readonly string PipeName =
        "QuickLook.App.Pipe." + (WindowsIdentity.GetCurrent().User?.Value ?? string.Empty);

    // IsAvailable() is called synchronously from CanPreview, on the UI thread, once per navigated-to
    // file -- during a burst of typing that's once per keystroke. A blocking pipe probe there (even a
    // successful one is a real cross-process round trip, not free) reads as UI stutter, and hammering the
    // pipe that rapidly also raises the odds of catching QuickLook's server between
    // Disconnect()/WaitForConnection() cycles (it handles one connection at a time) and reading that as a
    // spurious failure. So this never blocks the caller: it always returns the last known value instantly
    // and kicks off a background refresh, at most once per RefreshIntervalMs and never more than one at a
    // time, to keep that value from going stale for more than about a second.
    private const int RefreshIntervalMs = 1000;
    private static long _lastRefreshStartedTicks;
    private static int _refreshInFlight;
    private static volatile bool _cachedAvailable;

    // The one exception to "never blocks": _cachedAvailable defaults to false, so without this the very
    // first call in the process's lifetime would always report unavailable even if QuickLook is actually
    // running, since the background refresh hasn't had a chance to complete yet -- a real cold-start
    // failure, not a caching-staleness one, so a shorter TTL wouldn't have fixed it either. This blocks
    // synchronously exactly once (a bounded, one-time cost, not a per-navigation one) so that first real
    // answer is trustworthy; every call after it goes through the non-blocking path above.
    private static int _hasCheckedOnce;

    public static bool IsAvailable()
    {
        if (Interlocked.CompareExchange(ref _hasCheckedOnce, 1, 0) == 0)
        {
            _cachedAvailable = TrySend(PingMessage, string.Empty, string.Empty);
            Interlocked.Exchange(ref _lastRefreshStartedTicks, DateTime.UtcNow.Ticks);
            return _cachedAvailable;
        }

        MaybeStartBackgroundRefresh();
        return _cachedAvailable;
    }

    private static void MaybeStartBackgroundRefresh()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastRefreshStartedTicks);
        if (nowTicks - lastTicks < RefreshIntervalMs * TimeSpan.TicksPerMillisecond)
            return;

        if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0)
            return; // a refresh is already running

        Interlocked.Exchange(ref _lastRefreshStartedTicks, nowTicks);
        Task.Run(() =>
        {
            try { _cachedAvailable = TrySend(PingMessage, string.Empty, string.Empty); }
            finally { Interlocked.Exchange(ref _refreshInFlight, 0); }
        });
    }

    // Also called synchronously from the UI thread on every single navigation (CreatePreview/
    // TrySetTarget/EndPreviewSession) -- neither caller uses the bool result, so these are fire-and-
    // forget onto a background chain instead of blocking there too. Chained (not independent Task.Run
    // calls) specifically so ordering is preserved: typing fast enough to fire several of these in a row
    // must not let a later Invoke's send complete before an earlier one's, which would land QuickLook on
    // a stale file instead of the one currently selected.
    private static Task _sendChain = Task.CompletedTask;
    private static readonly object ChainLock = new();

    public static void TryInvokePreview(string path) => EnqueueSend(ToggleMessage, path, TopOption);

    public static void TryClosePreview() => EnqueueSend(CloseMessage, string.Empty, string.Empty);

    private static void EnqueueSend(string pipeMessage, string path, string options)
    {
        lock (ChainLock)
        {
            _sendChain = _sendChain.ContinueWith(_ => TrySend(pipeMessage, path, options), TaskScheduler.Default);
        }
    }

    private static bool TrySend(string pipeMessage, string path, string options)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(ConnectTimeoutMs);

            // QuickLook's server does an unconditional reader.ReadLine() with no null guard, so a client
            // that connects without ever writing a line crashes its read loop (NullReferenceException on
            // the null result) and takes down the pipe for the rest of that QuickLook session -- always
            // write a real line, even for the no-op ping.
            using var writer = new StreamWriter(client);
            writer.WriteLine($"{pipeMessage}|{path}|{options}");
            writer.Flush();
            Logger.Log($"[QuickLookBridge] pipe send ok: {pipeMessage} '{path}' options='{options}' (pipe='{PipeName}')", LogLevel.Info);
            return true;
        }
        catch (Exception ex)
        {
            // Temporary diagnostic (Info, not Warn/Debug) while chasing an inconsistent-availability
            // report -- narrow the log level back down once that's understood.
            Logger.Log($"[QuickLookBridge] pipe send FAILED: {pipeMessage} '{path}' options='{options}' (pipe='{PipeName}') -> {ex.GetType().Name}: {ex.Message}", LogLevel.Info);
            return false;
        }
    }
}
