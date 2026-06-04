using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SwiftList.Core.Hook;

namespace SwiftList.Core.Hook.InlineSearch
{
    public class KeyboardHookService : IDisposable
    {
        private IntPtr _hookId = IntPtr.Zero;
        private KeyboardNativeMethods.LowLevelKeyboardProc? _proc;
        private readonly ExplorerTracker _explorerTracker;
        private uint _lastCtrlDownTime;

        public event Action? OnDoubleCtrl;
        public event Action<char>? OnCharacterTyped;
        public event Action? OnBackspacePressed;
        public event Action? OnEscapePressed;
        public event Action? OnEnterPressed;
        public event Action? OnUpPressed;
        public event Action? OnDownPressed;
        public event Action? OnLeftPressed;
        public event Action? OnRightPressed;
        public event Action<int>? OnCtrlNumberPressed;

        public bool IsQuickSearchWindowVisible { get; set; }
        public bool IsInlineSearchVisible { get; set; }
        public uint AppProcessId { get; set; }

        public KeyboardHookService(ExplorerTracker explorerTracker)
        {
            _explorerTracker = explorerTracker;
        }

        public void Start()
        {
            if (_hookId != IntPtr.Zero) return;
            _proc = HookCallback;
            IntPtr hMod = KeyboardNativeMethods.GetModuleHandle(null);
            _hookId = KeyboardNativeMethods.SetWindowsHookEx(KeyboardNativeMethods.WH_KEYBOARD_LL, _proc, hMod, 0);
            if (_hookId == IntPtr.Zero)
            {
                Logger.Log($"[KeyboardHookService] Failed to install keyboard hook! Error={Marshal.GetLastWin32Error()}");
            }
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                KeyboardNativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (FullscreenHelper.IsForegroundWindowFullScreen())
            {
                return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            if (nCode >= 0 && (wParam == (IntPtr)KeyboardNativeMethods.WM_KEYDOWN || wParam == (IntPtr)KeyboardNativeMethods.WM_SYSKEYDOWN))
            {
                var hookStruct = Marshal.PtrToStructure<KeyboardNativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int vkCode = (int)hookStruct.vkCode;

                // 1. Detect double-Ctrl
                if (vkCode == 0x11 || vkCode == 0xA2 || vkCode == 0xA3) // VK_CONTROL / VK_LCONTROL / VK_RCONTROL
                {
                    uint currentTime = hookStruct.time;
                    uint elapsed = currentTime - _lastCtrlDownTime;
                    if (elapsed > 100 && elapsed < 500)
                    {
                        _lastCtrlDownTime = 0;
                        OnDoubleCtrl?.Invoke();
                    }
                    else
                    {
                        _lastCtrlDownTime = currentTime;
                    }
                }
                else
                {
                    _lastCtrlDownTime = 0;
                }

                // 2. Filter key inputs for Inline Search
                if (IsQuickSearchWindowVisible)
                {
                    return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                IntPtr fgHwnd = KeyboardNativeMethods.GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    KeyboardNativeMethods.GetWindowThreadProcessId(fgHwnd, out uint fgPid);
                    if (fgPid == AppProcessId || fgPid == (uint)Environment.ProcessId)
                    {
                        return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }
                }

                bool ctrlDown = (KeyboardNativeMethods.GetKeyState(KeyboardNativeMethods.VK_CONTROL) & 0x8000) != 0;
                bool altDown = (KeyboardNativeMethods.GetKeyState(KeyboardNativeMethods.VK_MENU) & 0x8000) != 0;
                bool winDown = (KeyboardNativeMethods.GetKeyState(KeyboardNativeMethods.VK_LWIN) & 0x8000) != 0 || 
                               (KeyboardNativeMethods.GetKeyState(KeyboardNativeMethods.VK_RWIN) & 0x8000) != 0;

                // Handle Ctrl+G inside file dialogs
                if (_explorerTracker.IsActiveWindowDialog && ctrlDown && !altDown && !winDown && vkCode == 0x47)
                {
                    string? lastExplorerPath = _explorerTracker.LastActiveExplorerPath;
                    if (!string.IsNullOrEmpty(lastExplorerPath) && Directory.Exists(lastExplorerPath))
                    {
                        IntPtr targetEdit = ExplorerTracker.FindSubEditBox(_explorerTracker.ActiveHwnd);
                        if (targetEdit != IntPtr.Zero)
                        {
                            string navPath = lastExplorerPath.EndsWith("\\") ? lastExplorerPath : lastExplorerPath + "\\";
                            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                            {
                                FileDialogNavigator.NavigateDialog(targetEdit, navPath);
                            });
                            return (IntPtr)1; // Consume key
                        }
                    }
                }

                // If text input is focused, bypass
                if (fgHwnd != IntPtr.Zero && FocusTargetEvaluator.IsForegroundTextInputFocused(fgHwnd))
                {
                    return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                if (_explorerTracker.IsActiveWindowDialog)
                {
                    return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                if (_explorerTracker.IsExplorerOrDesktopActive || IsInlineSearchVisible)
                {
                    if (!IsInlineSearchVisible &&
                        _explorerTracker.IsActiveWindowExplorer &&
                        !FocusTargetEvaluator.IsExplorerFileViewFocused(fgHwnd))
                    {
                        return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    if (ctrlDown && !altDown && !winDown && IsInlineSearchVisible)
                    {
                        int num = -1;
                        if (vkCode >= 0x31 && vkCode <= 0x39)
                            num = vkCode - 0x31 + 1;
                        else if (vkCode >= 0x61 && vkCode <= 0x69)
                            num = vkCode - 0x61 + 1;

                        if (num >= 1 && num <= 9)
                        {
                            OnCtrlNumberPressed?.Invoke(num);
                            return (IntPtr)1; // Consume
                        }
                    }

                    if (ctrlDown || altDown || winDown)
                    {
                        return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    if (vkCode == KeyboardNativeMethods.VK_ESCAPE)
                    {
                        if (IsInlineSearchVisible)
                        {
                            OnEscapePressed?.Invoke();
                            return (IntPtr)1;
                        }
                        return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    if (vkCode == KeyboardNativeMethods.VK_BACK && IsInlineSearchVisible)
                    {
                        OnBackspacePressed?.Invoke();
                        return (IntPtr)1;
                    }

                    if (vkCode == KeyboardNativeMethods.VK_RETURN && IsInlineSearchVisible)
                    {
                        OnEnterPressed?.Invoke();
                        return (IntPtr)1;
                    }

                    if (vkCode == KeyboardNativeMethods.VK_UP && IsInlineSearchVisible)
                    {
                        OnUpPressed?.Invoke();
                        return (IntPtr)1;
                    }

                    if (vkCode == KeyboardNativeMethods.VK_DOWN && IsInlineSearchVisible)
                    {
                        OnDownPressed?.Invoke();
                        return (IntPtr)1;
                    }

                    if (vkCode == KeyboardNativeMethods.VK_LEFT && IsInlineSearchVisible)
                    {
                        OnLeftPressed?.Invoke();
                        return (IntPtr)1;
                    }

                    if (vkCode == KeyboardNativeMethods.VK_RIGHT && IsInlineSearchVisible)
                    {
                        OnRightPressed?.Invoke();
                        return (IntPtr)1;
                    }

                    if (vkCode == KeyboardNativeMethods.VK_TAB)
                    {
                        return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    bool isTriggerKey = (vkCode == KeyboardNativeMethods.VK_PROCESSKEY) ||
                                        (vkCode >= 0x41 && vkCode <= 0x5A) ||
                                        (vkCode >= 0x30 && vkCode <= 0x39) ||
                                        (vkCode >= 0x60 && vkCode <= 0x69);

                    if (isTriggerKey && !IsInlineSearchVisible)
                    {
                        bool isChineseImeActive = false;
                        if (fgHwnd != IntPtr.Zero)
                        {
                            uint fgThread = KeyboardNativeMethods.GetWindowThreadProcessId(fgHwnd, out _);
                            if (fgThread != 0)
                            {
                                IntPtr hkl = KeyboardNativeMethods.GetKeyboardLayout(fgThread);
                                int langId = (int)((long)hkl & 0xFFFF);
                                if (langId == 0x0804 || langId == 0x0404 || langId == 0x0C04 || langId == 0x1004 || langId == 0x1404)
                                {
                                    isChineseImeActive = true;
                                }
                            }
                        }

                        if (isChineseImeActive || vkCode == KeyboardNativeMethods.VK_PROCESSKEY)
                        {
                            OnCharacterTyped?.Invoke('\0');
                        }
                        else
                        {
                            var keyboardState = new byte[256];
                            KeyboardNativeMethods.GetKeyboardState(keyboardState);
                            var sb = new StringBuilder(2);
                            int result = KeyboardNativeMethods.ToUnicode(hookStruct.vkCode, hookStruct.scanCode, keyboardState, sb, sb.Capacity, 0);
                            if (result == 1 && !char.IsControl(sb[0]))
                            {
                                OnCharacterTyped?.Invoke(sb[0]);
                            }
                            else
                            {
                                OnCharacterTyped?.Invoke('\0');
                            }
                        }
                        return (IntPtr)1;
                    }
                }
            }

            return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
