namespace SwiftList.Core.Hook;

// Correlates the App-side blocking ExecuteItem call with the Hook's async reply over the event pipe --
// same request/response-over-a-fire-and-forget-pipe shape as ListIpcCoordinator, kept separate since this
// is IInlineSearchAdapter-specific rather than generic list-control interop.
public static class InlineAdapterIpcCoordinator
{
    private static readonly object _lock = new();
    private static AutoResetEvent? _executeItemEvent;
    private static bool _executeItemResult;
    // Echoed back on the response and checked in SetExecuteItemResult so a reply that arrives after its
    // own call already gave up on WaitOne (timed out) can't be misattributed to a later, unrelated call --
    // both share the same static fields since only one ExecuteItem can be in flight at a time (the lock).
    private static int _pendingRequestId;
    private static int _nextRequestId;

    // A live hook answers a same-machine named-pipe round trip in low single-digit milliseconds; 1s bounds
    // the worst case (hook busy, or briefly unreachable during a cold start) without stalling the caller's
    // UI thread for long -- ExecuteItem is on the "press Enter to navigate" hot path.
    public static bool ExecuteItem(IntPtr hwnd, string path, string searchInput, Action<IpcMessage> sendMsg)
    {
        lock (_lock)
        {
            using var evt = new AutoResetEvent(false);
            var requestId = ++_nextRequestId;
            _pendingRequestId = requestId;
            _executeItemEvent = evt;
            _executeItemResult = false;

            sendMsg(new IpcMessage { Id = IpcMessageId.ExecuteInlineItem, Hwnd = hwnd.ToInt64(), StringVal1 = path, StringVal2 = searchInput, IntVal = requestId });

            return evt.WaitOne(1000) && _executeItemResult;
        }
    }

    public static void SetExecuteItemResult(int requestId, bool result)
    {
        if (requestId != _pendingRequestId) return; // stale reply for a call we already gave up on
        _executeItemResult = result;
        try { _executeItemEvent?.Set(); } catch { }
    }
}
