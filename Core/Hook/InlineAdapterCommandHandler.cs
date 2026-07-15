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
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    // Adapter code is third-party-plugin-authored native/COM interop -- an uncaught
                    // exception on a ThreadPool thread would take down the whole hook process (and every
                    // user's global hotkeys with it), so this must never propagate. On failure, still send
                    // a response so the App's blocking ExecuteItem call fails fast instead of timing out.
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
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { ResolveAdapter(process, hwnd)?.OnSelectionChanged(hwnd, selectedPath); }
                    catch (Exception ex) { Logger.Log($"[InlineAdapterCommandHandler] OnSelectionChanged threw: {ex.Message}", LogLevel.Error); }
                });
                break;

            case IpcMessageId.InlineSearchFinished:
                var executed = msg.BoolVal;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { ResolveAdapter(process, hwnd)?.OnSearchFinished(hwnd, executed); }
                    catch (Exception ex) { Logger.Log($"[InlineAdapterCommandHandler] OnSearchFinished threw: {ex.Message}", LogLevel.Error); }
                });
                break;
        }
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
