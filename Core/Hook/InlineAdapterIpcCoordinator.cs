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
    //
    // isDir is the caller's own already-known answer (from search-result/menu-item metadata, e.g.
    // AppSearchResult.IsDir or QuickNavigationMenu's item.HasSubMenu) for whether path is a directory.
    // Adapters no longer call Directory.Exists/File.Exists themselves to figure this out -- the Hook
    // process runs elevated for admin users, and UAC's split token puts an elevated process in a different
    // logon session than the one that mapped any network drive letters, so a mapped-drive path that's
    // perfectly valid in the caller's own (never-elevated) session would silently resolve to "doesn't
    // exist" from inside the Hook. Baked into the path itself (a trailing separator marks a directory,
    // matching Path.EndsInDirectorySeparator) rather than a new IpcMessage field, since every adapter reads
    // path as a plain string already and this needs no protocol/interface change.
    public static bool ExecuteItem(IntPtr hwnd, string path, bool isDir, string searchInput, Action<IpcMessage> sendMsg)
    {
        var normalizedPath = NormalizePath(path, isDir);

        lock (_lock)
        {
            using var evt = new AutoResetEvent(false);
            var requestId = ++_nextRequestId;
            _pendingRequestId = requestId;
            _executeItemEvent = evt;
            _executeItemResult = false;

            sendMsg(new IpcMessage { Id = IpcMessageId.ExecuteInlineItem, Hwnd = hwnd.ToInt64(), StringVal1 = normalizedPath, StringVal2 = searchInput, IntVal = requestId });

            return evt.WaitOne(1000) && _executeItemResult;
        }
    }

    // Path.EndsInDirectorySeparator/TrimEndingDirectorySeparator, not a manual TrimEnd -- a bare TrimEnd
    // would eat a drive/UNC root's own separator too (e.g. "C:\" -> "C:", which no longer means the root),
    // where these two treat the root's separator as non-optional.
    private static string NormalizePath(string path, bool isDir)
    {
        if (isDir)
            return Path.EndsInDirectorySeparator(path) ? path : path + "\\";
        return Path.TrimEndingDirectorySeparator(path);
    }

    public static void SetExecuteItemResult(int requestId, bool result)
    {
        if (requestId != _pendingRequestId) return; // stale reply for a call we already gave up on
        _executeItemResult = result;
        try { _executeItemEvent?.Set(); } catch { }
    }
}
