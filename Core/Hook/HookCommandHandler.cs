using System;
using System.Text;
using SwiftList.Core.Hook.InlineSearch;
using SwiftList.PluginSdk;

namespace SwiftList.Core.Hook
{
    public sealed class HookCommandHandler
    {
        private readonly HookProcess _process;

        public HookCommandHandler(HookProcess process)
        {
            _process = process;
        }

        public void HandleAppCommand(IpcMessage msg)
        {
            try
            {
                switch (msg.Id)
                {
                    case IpcMessageId.SetQuickSearchVisible:
                        if (_process.KeyboardHook != null)
                            _process.KeyboardHook.IsQuickSearchWindowVisible = msg.BoolVal;
                        break;
                    case IpcMessageId.SetInlineSearchVisible:
                        if (_process.KeyboardHook != null)
                            _process.KeyboardHook.IsInlineSearchVisible = msg.BoolVal;
                        break;
                    case IpcMessageId.SetAppProcessId:
                        _process.AppProcessId = msg.ProcessId;
                        if (_process.KeyboardHook != null)
                            _process.KeyboardHook.AppProcessId = msg.ProcessId;
                        if (_process.ExplorerTracker != null)
                            _process.ExplorerTracker.AppProcessId = msg.ProcessId;
                        break;
                    case IpcMessageId.NavigateDialog:
                        {
                            IntPtr dialogHwnd = (IntPtr)msg.Hwnd;
                            string? navPath = msg.StringVal1;
                            if (dialogHwnd != IntPtr.Zero && !string.IsNullOrEmpty(navPath))
                            {
                                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                                {
                                    var adapter = (_process.ExplorerTracker != null && _process.ExplorerTracker.ActiveHwnd == dialogHwnd)
                                        ? _process.ExplorerTracker.ActiveAdapter
                                        : null;

                                    if (adapter == null)
                                    {
                                        var sbClass = new StringBuilder(256);
                                        ExplorerNativeHooks.GetClassName(dialogHwnd, sbClass, sbClass.Capacity);
                                        string className = sbClass.ToString();
                                        string processName = "Unknown";
                                        try
                                        {
                                            ExplorerNativeHooks.GetWindowThreadProcessId(dialogHwnd, out uint pid);
                                            if (pid != 0)
                                            {
                                                using (var proc = System.Diagnostics.Process.GetProcessById((int)pid))
                                                    processName = proc.ProcessName;
                                            }
                                        }
                                        catch { }
                                        adapter = FileDialogAdapterRegistry.GetMatchingAdapter(dialogHwnd, className, processName);
                                    }

                                    if (adapter != null)
                                    {
                                        adapter.NavigateTo(dialogHwnd, navPath);
                                    }
                                });
                            }
                        }
                        break;
                    case IpcMessageId.RestoreDialogFocus:
                        {
                            IntPtr activeHwnd = (IntPtr)msg.Hwnd;
                            if (activeHwnd != IntPtr.Zero)
                            {
                                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                                {
                                    var adapter = (_process.ExplorerTracker != null && _process.ExplorerTracker.ActiveHwnd == activeHwnd)
                                        ? _process.ExplorerTracker.ActiveAdapter
                                        : null;

                                    if (adapter == null)
                                    {
                                        var sbClass = new StringBuilder(256);
                                        ExplorerNativeHooks.GetClassName(activeHwnd, sbClass, sbClass.Capacity);
                                        string className = sbClass.ToString();
                                        string processName = "Unknown";
                                        try
                                        {
                                            ExplorerNativeHooks.GetWindowThreadProcessId(activeHwnd, out uint pid);
                                            if (pid != 0)
                                            {
                                                using (var proc = System.Diagnostics.Process.GetProcessById((int)pid))
                                                    processName = proc.ProcessName;
                                            }
                                        }
                                        catch { }
                                        adapter = FileDialogAdapterRegistry.GetMatchingAdapter(activeHwnd, className, processName);
                                    }

                                    if (adapter != null)
                                    {
                                        adapter.RestoreFocus(activeHwnd);
                                    }
                                });
                            }
                        }
                        break;

                    case IpcMessageId.ReloadSettings:
                        {
                            var newSettings = UserSettings.ForceReload();
                            if (Enum.TryParse<LogLevel>(newSettings.LogLevel, ignoreCase: true, out var newLogLevel))
                                Logger.MinimumLevel = newLogLevel;
                            if (_process.KeyboardHook != null)
                                _process.KeyboardHook.ReloadSettings();
                        }
                        break;
                    case IpcMessageId.SetHotkeysDisabled:
                        _process.IsHotkeysDisabledTemporarily = msg.BoolVal;
                        if (_process.KeyboardHook != null)
                            _process.KeyboardHook.IsHotkeysDisabledTemporarily = msg.BoolVal;
                        break;
                    case IpcMessageId.GetListItems:
                        {
                            IntPtr hwnd = (IntPtr)msg.Hwnd;
                            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                            {
                                var items = ElevatedListControlHelper.GetListItems(hwnd);
                                int chunkSize = 500;
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
                                    for (int i = 0; i < items.Count; i += chunkSize)
                                    {
                                        int count = Math.Min(chunkSize, items.Count - i);
                                        var chunk = new string[count];
                                        items.CopyTo(i, chunk, 0, count);
                                        bool isFinal = (i + count >= items.Count);

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
                            IntPtr hwnd = (IntPtr)msg.Hwnd;
                            string className = msg.StringVal1 ?? string.Empty;
                            int index = msg.IntVal;
                            bool clearOthers = msg.BoolVal;
                            bool selectState = msg.IsDesktop;
                            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                            {
                                ElevatedListControlHelper.SelectItem(hwnd, className, index, clearOthers, selectState);
                            });
                        }
                        break;
                    case IpcMessageId.ClearSelection:
                        {
                            IntPtr hwnd = (IntPtr)msg.Hwnd;
                            string className = msg.StringVal1 ?? string.Empty;
                            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                            {
                                ElevatedListControlHelper.ClearSelection(hwnd, className);
                            });
                        }
                        break;
                    case IpcMessageId.GetSelectedIndices:
                        {
                            IntPtr hwnd = (IntPtr)msg.Hwnd;
                            string className = msg.StringVal1 ?? string.Empty;
                            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
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
}
