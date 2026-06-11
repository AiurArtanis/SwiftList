using System.Text;
using SwiftList.Core.Hook.InlineSearch;
using SwiftList.PluginSdk;

namespace SwiftList.Core.Hook;

public sealed class HookCommandHandler
{
    private readonly HookProcess _process;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool fAttach);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public HookCommandHandler(HookProcess process) => _process = process;

    public void HandleAppCommand(IpcMessage msg)
    {
        try
        {
            switch (msg.Id)
            {
                case IpcMessageId.SetQuickSearchVisible:
                    _process.KeyboardHook?.IsQuickSearchWindowVisible = msg.BoolVal;
                    break;
                case IpcMessageId.SetInlineSearchVisible:
                    _process.KeyboardHook?.IsInlineSearchVisible = msg.BoolVal;
                    break;
                case IpcMessageId.SetAppProcessId:
                    _process.AppProcessId = msg.ProcessId;
                    _process.KeyboardHook?.AppProcessId = msg.ProcessId;
                    _process.ExplorerTracker?.AppProcessId = msg.ProcessId;
                    break;
                case IpcMessageId.NavigateDialog:
                    {
                        var dialogHwnd = (IntPtr)msg.Hwnd;
                        var navPath = msg.StringVal1;
                        if (dialogHwnd != IntPtr.Zero && !string.IsNullOrEmpty(navPath))
                        {
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                var adapter = (_process.ExplorerTracker != null && _process.ExplorerTracker.ActiveHwnd == dialogHwnd)
                                    ? _process.ExplorerTracker.ActiveAdapter
                                    : null;

                                if (adapter == null)
                                {
                                    var sbClass = new StringBuilder(256);
                                    ExplorerNativeHooks.GetClassName(dialogHwnd, sbClass, sbClass.Capacity);
                                    var className = sbClass.ToString();
                                    var processName = "Unknown";
                                    try
                                    {
                                        ExplorerNativeHooks.GetWindowThreadProcessId(dialogHwnd, out var pid);
                                        if (pid != 0)
                                        {
                                            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                                            processName = proc.ProcessName;
                                        }
                                    }
                                    catch { }
                                    adapter = FileDialogAdapterRegistry.GetMatchingAdapter(dialogHwnd, className, processName);
                                }

                                adapter?.NavigateTo(dialogHwnd, navPath);
                            });
                        }
                    }
                    break;
                case IpcMessageId.RestoreDialogFocus:
                    {
                        var activeHwnd = (IntPtr)msg.Hwnd;
                        if (activeHwnd != IntPtr.Zero)
                        {
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                var adapter = (_process.ExplorerTracker != null && _process.ExplorerTracker.ActiveHwnd == activeHwnd)
                                    ? _process.ExplorerTracker.ActiveAdapter
                                    : null;

                                if (adapter == null)
                                {
                                    var sbClass = new StringBuilder(256);
                                    ExplorerNativeHooks.GetClassName(activeHwnd, sbClass, sbClass.Capacity);
                                    var className = sbClass.ToString();
                                    var processName = "Unknown";
                                    try
                                    {
                                        ExplorerNativeHooks.GetWindowThreadProcessId(activeHwnd, out var pid);
                                        if (pid != 0)
                                        {
                                            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                                            processName = proc.ProcessName;
                                        }
                                    }
                                    catch { }
                                    adapter = FileDialogAdapterRegistry.GetMatchingAdapter(activeHwnd, className, processName);
                                }

                                adapter?.RestoreFocus(activeHwnd);
                            });
                        }
                    }
                    break;

                case IpcMessageId.ForceForeground:
                    {
                        var appHwnd = (IntPtr)msg.Hwnd;
                        if (appHwnd != IntPtr.Zero)
                        {
                            Logger.Log($"[HookCommandHandler] Forcing foreground for HWND 0x{appHwnd.ToInt64():X}", LogLevel.Debug);

                            // Simulate Alt key press to bypass SetForegroundWindow restrictions
                            keybd_event(VK_MENU, 0, 0, IntPtr.Zero);
                            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, IntPtr.Zero);

                            var fgHwnd = GetForegroundWindow();
                            var fgThreadId = GetWindowThreadProcessId(fgHwnd, out _);
                            var currentThreadId = GetCurrentThreadId();

                            var attached = false;
                            if (fgThreadId != 0 && fgThreadId != currentThreadId)
                            {
                                attached = AttachThreadInput(currentThreadId, fgThreadId, true);
                            }

                            try
                            {
                                SetForegroundWindow(appHwnd);
                                SetActiveWindow(appHwnd);
                                SetFocus(appHwnd);
                            }
                            finally
                            {
                                if (attached)
                                {
                                    AttachThreadInput(currentThreadId, fgThreadId, false);
                                }
                            }
                        }
                    }
                    break;

                case IpcMessageId.ReloadSettings:
                    {
                        var newSettings = UserSettings.ForceReload();
                        if (Enum.TryParse<LogLevel>(newSettings.LogLevel, ignoreCase: true, out var newLogLevel))
                            Logger.MinimumLevel = newLogLevel;
                        _process.KeyboardHook?.ReloadSettings();
                    }
                    break;
                case IpcMessageId.SetHotkeysDisabled:
                    _process.IsHotkeysDisabledTemporarily = msg.BoolVal;
                    _process.KeyboardHook?.IsHotkeysDisabledTemporarily = msg.BoolVal;
                    break;
                case IpcMessageId.GetListItems:
                    {
                        var hwnd = (IntPtr)msg.Hwnd;
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            var items = ElevatedListControlHelper.GetListItems(hwnd);
                            var chunkSize = 500;
                            if (items == null || items.Count == 0)
                            {
                                _process.IpcServer.SendMessage(new IpcMessage
                                {
                                    Id = IpcMessageId.GetListItemsResponse,
                                    StringArray = Array.Empty<string>(),
                                    BoolVal = true
                                });
                            }
                            else
                            {
                                for (var i = 0; i < items.Count; i += chunkSize)
                                {
                                    var count = Math.Min(chunkSize, items.Count - i);
                                    var chunk = new string[count];
                                    items.CopyTo(i, chunk, 0, count);
                                    var isFinal = (i + count >= items.Count);

                                    _process.IpcServer.SendMessage(new IpcMessage
                                    {
                                        Id = IpcMessageId.GetListItemsResponse,
                                        StringArray = chunk,
                                        BoolVal = isFinal
                                    });
                                }
                            }
                        });
                    }
                    break;
                case IpcMessageId.SelectItem:
                    {
                        var hwnd = (IntPtr)msg.Hwnd;
                        var className = msg.StringVal1 ?? string.Empty;
                        var index = msg.IntVal;
                        var clearOthers = msg.BoolVal;
                        var selectState = msg.IsDesktop;
                        ThreadPool.QueueUserWorkItem(_ => ElevatedListControlHelper.SelectItem(hwnd, className, index, clearOthers, selectState));
                    }
                    break;
                case IpcMessageId.ClearSelection:
                    {
                        var hwnd = (IntPtr)msg.Hwnd;
                        var className = msg.StringVal1 ?? string.Empty;
                        ThreadPool.QueueUserWorkItem(_ => ElevatedListControlHelper.ClearSelection(hwnd, className));
                    }
                    break;
                case IpcMessageId.GetSelectedIndices:
                    {
                        var hwnd = (IntPtr)msg.Hwnd;
                        var className = msg.StringVal1 ?? string.Empty;
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            var indices = ElevatedListControlHelper.GetSelectedIndices(hwnd, className);
                            _process.IpcServer.SendMessage(new IpcMessage
                            {
                                Id = IpcMessageId.GetSelectedIndicesResponse,
                                IntArray = indices.ToArray()
                            });
                        });
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[HookCommandHandler] Error parsing IPC command {msg.Id}: {ex.Message}", LogLevel.Warn);
        }
    }
}
