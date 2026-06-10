using System.Text;
using SwiftList.Core.Hook.InlineSearch;
using SwiftList.PluginSdk;

namespace SwiftList.Core.Hook;

public sealed class HookCommandHandler
{
    private readonly HookProcess _process;

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
