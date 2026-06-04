using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SwiftList.Core.Hook.InlineSearch;

namespace SwiftList.Core.Hook
{
    public sealed class HookProcess : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(int idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYUP = 0x0002;


        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        private const uint WM_QUIT = 0x0012;

        private readonly HookIpcServer _ipcServer;
        private ExplorerTracker? _explorerTracker;
        private KeyboardHookService? _keyboardHook;
        private MouseHookService? _mouseHook;

        private int _nativeThreadId;
        private volatile bool _running;

        public HookProcess(HookIpcServer ipcServer)
        {
            _ipcServer = ipcServer;

            _ipcServer.OnStopRequested += () => Stop();
            _ipcServer.OnCommandReceived += HandleAppCommand;
        }

        private void HandleAppCommand(IpcMessage msg)
        {
            try
            {
                switch (msg.Id)
                {
                    case IpcMessageId.SetQuickSearchVisible:
                        if (_keyboardHook != null) _keyboardHook.IsQuickSearchWindowVisible = msg.BoolVal;
                        break;
                    case IpcMessageId.SetInlineSearchVisible:
                        if (_keyboardHook != null) _keyboardHook.IsInlineSearchVisible = msg.BoolVal;
                        break;
                    case IpcMessageId.SetAppProcessId:
                        if (_keyboardHook != null) _keyboardHook.AppProcessId = msg.ProcessId;
                        if (_explorerTracker != null) _explorerTracker.AppProcessId = msg.ProcessId;
                        break;
                    case IpcMessageId.NavigateDialog:
                        {
                            IntPtr targetEdit = (IntPtr)msg.Hwnd;
                            string? navPath = msg.StringVal1;
                            if (targetEdit != IntPtr.Zero && !string.IsNullOrEmpty(navPath))
                            {
                                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                                {
                                    FileDialogNavigator.NavigateDialog(targetEdit, navPath);
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
                                    IntPtr targetEdit = ExplorerNativeHooks.FindSubEditBox(activeHwnd);
                                    if (targetEdit == IntPtr.Zero) return;

                                    uint targetThread = ExplorerNativeHooks.GetWindowThreadProcessId(targetEdit, out uint _);
                                    uint currentThread = ExplorerNativeHooks.GetCurrentThreadId();
                                    bool attached = false;
                                    try
                                    {
                                        // Send a dummy key event so Windows grants this thread foreground permission,
                                        // bypassing the foreground-lock that was set when the low-privilege inline
                                        // window was the foreground window.
                                        keybd_event(0xFF, 0, 0, UIntPtr.Zero);
                                        keybd_event(0xFF, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                                        if (targetThread != 0 && targetThread != currentThread)
                                        {
                                            attached = ExplorerNativeHooks.AttachThreadInput(currentThread, targetThread, true);
                                        }

                                        ExplorerNativeHooks.SetForegroundWindow(activeHwnd);
                                        ExplorerNativeHooks.SetFocus(targetEdit);
                                        ExplorerNativeHooks.PostMessage(targetEdit, ExplorerNativeHooks.WM_LBUTTONDOWN, (IntPtr)1, IntPtr.Zero);
                                        ExplorerNativeHooks.PostMessage(targetEdit, ExplorerNativeHooks.WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
                                        ExplorerNativeHooks.PostMessage(targetEdit, ExplorerNativeHooks.EM_SETSEL, IntPtr.Zero, (IntPtr)(-1));
                                    }
                                    finally
                                    {
                                        if (attached)
                                        {
                                            ExplorerNativeHooks.AttachThreadInput(currentThread, targetThread, false);
                                        }
                                    }
                                });
                            }
                        }
                        break;

                    case IpcMessageId.ReloadSettings:
                        if (_keyboardHook != null) _keyboardHook.ReloadSettings();
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[HookProcess] Error parsing IPC command {msg.Id}: {ex.Message}", LogLevel.Warn);
            }
        }

        public void RunMessageLoop()
        {
            _nativeThreadId = GetCurrentThreadId();

            try
            {
                // 1. Start Explorer Tracker
                _explorerTracker = new ExplorerTracker();
                _explorerTracker.OnExplorerActivated += (hwnd, title, className, isDesktop) =>
                {
                    _ipcServer.SendMessage(new IpcMessage
                    {
                        Id = IpcMessageId.ExplorerActivated,
                        Hwnd = hwnd.ToInt64(),
                        StringVal1 = title,
                        StringVal2 = className,
                        IsDesktop = isDesktop
                    });
                };
                _explorerTracker.OnExplorerDeactivated += () =>
                {
                    _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.ExplorerDeactivated });
                    // Trim working set on deactivation to optimize memory footprint when not interacting
                    System.Threading.Tasks.Task.Run(() => {
                        try { Win32Api.TrimWorkingSet(); } catch { }
                    });
                };
                _explorerTracker.OnPathCaptured += (path, isDesktop) =>
                {
                    _ipcServer.SendMessage(new IpcMessage
                    {
                        Id = IpcMessageId.PathCaptured,
                        StringVal1 = path,
                        IsDesktop = isDesktop
                    });
                };
                _explorerTracker.OnActiveWindowMoved += () =>
                {
                    _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.ActiveWindowMoved });
                };
                _explorerTracker.OnError += (msg) =>
                {
                    _ipcServer.SendMessage(new IpcMessage
                    {
                        Id = IpcMessageId.Error,
                        StringVal1 = msg
                    });
                };
                _explorerTracker.Start();

                // 2. Start Keyboard Hook
                _keyboardHook = new KeyboardHookService(_explorerTracker);
                _keyboardHook.OnDoubleCtrl += () =>
                {
                    Logger.Log("[HookProcess] Double-Ctrl detected, sending ACTIVATE.", LogLevel.Debug);
                    _ipcServer.SendActivate();
                };
                _keyboardHook.OnCharacterTyped += ch => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyChar, CharVal = ch });
                _keyboardHook.OnBackspacePressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyBackspace });
                _keyboardHook.OnEscapePressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyEscape });
                _keyboardHook.OnEnterPressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyEnter });
                _keyboardHook.OnUpPressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyUp });
                _keyboardHook.OnDownPressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyDown });
                _keyboardHook.OnLeftPressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyLeft });
                _keyboardHook.OnRightPressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyRight });
                _keyboardHook.OnCtrlNumberPressed += num => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyCtrlNumber, IntVal = num });
                _keyboardHook.Start();

                // 3. Start Mouse Hook
                _mouseHook = new MouseHookService();
                _mouseHook.OnMouseClick += (x, y) => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.MouseClick, MouseX = x, MouseY = y });
                _mouseHook.Start();

                Logger.Log("[HookProcess] Hooks and ExplorerTracker initialized successfully.", LogLevel.Info);
                _running = true;

                while (_running)
                {
                    int result = GetMessage(out MSG msg, IntPtr.Zero, 0, 0);
                    if (result <= 0) break;
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            finally
            {
                CleanupHooks();
            }
        }

        private void CleanupHooks()
        {
            if (_keyboardHook != null)
            {
                _keyboardHook.Dispose();
                _keyboardHook = null;
            }
            if (_mouseHook != null)
            {
                _mouseHook.Dispose();
                _mouseHook = null;
            }
            if (_explorerTracker != null)
            {
                _explorerTracker.Dispose();
                _explorerTracker = null;
            }
            Logger.Log("[HookProcess] Hooks and ExplorerTracker stopped/cleaned up.", LogLevel.Info);
            
            try
            {
                Win32Api.TrimWorkingSet();
            }
            catch { }
        }

        public void Stop()
        {
            _running = false;
            if (_nativeThreadId != 0)
            {
                PostThreadMessage(_nativeThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
