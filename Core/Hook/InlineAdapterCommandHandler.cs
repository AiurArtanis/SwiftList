using System.Text;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Registries;

namespace SwiftList.Core.Hook;

// Split out of HookCommandHandler to keep that file under the line-count limit. Runs
// IInlineSearchAdapter's write-side methods (ExecuteItem/OnSelectionChanged/OnSearchFinished) here in the
// Hook process instead of the App process, so navigating a third-party file manager (Total Commander,
// Directory Opus, ...) still works when that file manager is running elevated and the App -- which never
// elevates itself -- would otherwise have its window messages silently dropped by UIPI. Mirrors the
// resolve-then-dispatch shape HookCommandHandler already uses for NavigateDialog/RestoreDialogFocus.
//
// Each call runs on its own freshly-spun-up STA thread (RunOnSta), not ThreadPool.QueueUserWorkItem and
// NOT the tracker thread that owns ExplorerTracker: some adapters (Explorer's IShellWindows/Navigate2/
// SelectItem, OneCommander's UI Automation) are STA-affine COM interop, so an MTA ThreadPool thread would
// force COM to marshal across apartments. A dedicated thread per call, rather than routing through the
// tracker thread, matters because at least one adapter (Total Commander) calls plain SendMessage with no
// timeout (see TotalCommander/Win32/Win32Helper.cs) -- if that target hangs, only this one call's thread
// leaks/blocks; ExplorerTracker's own WinEvent-based foreground/focus tracking (which runs on the tracker
// thread) keeps working for every other window and adapter regardless.
internal static class InlineAdapterCommandHandler
{
    public static void Handle(HookProcess process, IpcMessage msg)
    {
        var hwnd = (IntPtr)msg.Hwnd;
        if (hwnd == IntPtr.Zero) return;

        switch (msg.Id)
        {
            case IpcMessageId.ExecuteInlineItem:
                var path = msg.StringVal1 ?? string.Empty;
                var searchInput = msg.StringVal2 ?? string.Empty;
                var requestId = msg.IntVal;
                RunOnSta(() =>
                {
                    // Adapter code is third-party-plugin-authored native/COM interop -- an uncaught
                    // exception here would take down whatever thread it ran on, so this must never
                    // propagate. On failure, still send a response so the App's blocking ExecuteItem call
                    // fails fast instead of timing out.
                    var result = false;
                    try
                    {
                        result = ResolveAdapter(process, hwnd)?.ExecuteItem(hwnd, path, searchInput) ?? false;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[InlineAdapterCommandHandler] ExecuteItem threw: {ex.Message}", LogLevel.Error);
                    }
                    process.IpcServer.SendMessage(new IpcMessage { Id = IpcMessageId.ExecuteInlineItemResponse, IntVal = requestId, BoolVal = result });
                });
                break;

            case IpcMessageId.InlineSelectionChanged:
                var selectedPath = msg.StringVal1 ?? string.Empty;
                RunOnSta(() =>
                {
                    try { ResolveAdapter(process, hwnd)?.OnSelectionChanged(hwnd, selectedPath); }
                    catch (Exception ex) { Logger.Log($"[InlineAdapterCommandHandler] OnSelectionChanged threw: {ex.Message}", LogLevel.Error); }
                });
                break;

            case IpcMessageId.InlineSearchFinished:
                var executed = msg.BoolVal;
                RunOnSta(() =>
                {
                    try { ResolveAdapter(process, hwnd)?.OnSearchFinished(hwnd, executed); }
                    catch (Exception ex) { Logger.Log($"[InlineAdapterCommandHandler] OnSearchFinished threw: {ex.Message}", LogLevel.Error); }
                });
                break;
        }
    }

    private static void RunOnSta(Action action)
    {
        var thread = new Thread(() =>
        {
            // Belt-and-suspenders: every caller already wraps its own logic in try/catch, but an
            // exception escaping this thread's entry point entirely (e.g. from the catch block's own
            // Logger.Log call) would otherwise crash the whole process, same as any other unhandled
            // exception on a non-pooled thread.
            try { action(); }
            catch (Exception ex) { Logger.Log($"[InlineAdapterCommandHandler] STA thread threw: {ex.Message}", LogLevel.Error); }
        })
        {
            IsBackground = true,
            Name = "InlineAdapterSta"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static IInlineSearchAdapter? ResolveAdapter(HookProcess process, IntPtr hwnd)
    {
        if (process.ExplorerTracker != null && process.ExplorerTracker.ActiveHwnd == hwnd)
            return process.ExplorerTracker.ActiveInlineAdapter;

        var sbClass = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
        var className = sbClass.ToString();
        var processName = "Unknown";
        try
        {
            ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != 0)
            {
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
        }
        catch { }
        return InlineSearchAdapterRegistry.GetMatchingAdapter(hwnd, className, processName);
    }
}
