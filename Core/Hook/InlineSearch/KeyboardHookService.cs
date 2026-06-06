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
        
        private UserSettings _settings = UserSettings.Load();
        private GlobalHotkeyDetector _hotkeyDetector;

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
        public bool IsHotkeysDisabledTemporarily { get; set; }

        public KeyboardHookService(ExplorerTracker explorerTracker)
        {
            _explorerTracker = explorerTracker;
            _hotkeyDetector = new GlobalHotkeyDetector(_settings, _explorerTracker);
        }

        public void ReloadSettings()
        {
            _settings = UserSettings.ForceReload();
            _hotkeyDetector = new GlobalHotkeyDetector(_settings, _explorerTracker);
            Logger.Log("[KeyboardHookService] Hotkey settings reloaded.", LogLevel.Info);
        }

        public void Start()
        {
            if (_hookId != IntPtr.Zero) return;
            _proc = HookCallback;
            IntPtr hMod = KeyboardNativeMethods.GetModuleHandle(null);
            _hookId = KeyboardNativeMethods.SetWindowsHookEx(KeyboardNativeMethods.WH_KEYBOARD_LL, _proc, hMod, 0);
            if (_hookId == IntPtr.Zero)
            {
                Logger.Log($"[KeyboardHookService] Failed to install keyboard hook! Error={Marshal.GetLastWin32Error()}", SwiftList.Core.LogLevel.Error);
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
                uint time = hookStruct.time;

                // 1. Detect Toggle Window Hotkey
                bool bypassToggleHotkey = IsHotkeysDisabledTemporarily || KeyboardUtils.IsForegroundProcessBlacklisted(_settings.BlacklistedProcesses);
                if (!bypassToggleHotkey && _hotkeyDetector.CheckToggleWindowHotkey(vkCode, time, out bool consumeToggleKey, OnDoubleCtrl))
                {
                    if (consumeToggleKey)
                    {
                        return (IntPtr)1;
                    }
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

                // 3. Detect and handle Quick Switch Hotkey
                if (_hotkeyDetector.CheckAndHandleQuickSwitch(vkCode, time, out bool consumeQuickSwitchKey))
                {
                    if (consumeQuickSwitchKey)
                    {
                        return (IntPtr)1;
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

                // 4. Handle Inline Search key events
                if (HandleInlineSearchKeys(vkCode, hookStruct, fgHwnd))
                {
                    return (IntPtr)1;
                }
            }

            return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private bool HandleInlineSearchKeys(int vkCode, KeyboardNativeMethods.KBDLLHOOKSTRUCT hookStruct, IntPtr fgHwnd)
        {
            IntPtr targetFocus = fgHwnd;
            uint threadId = KeyboardNativeMethods.GetWindowThreadProcessId(fgHwnd, out uint fgPid);
            var guiInfo = new KeyboardNativeMethods.GUITHREADINFO
            {
                cbSize = Marshal.SizeOf<KeyboardNativeMethods.GUITHREADINFO>()
            };
            if (KeyboardNativeMethods.GetGUIThreadInfo(threadId, ref guiInfo) && guiInfo.hwndFocus != IntPtr.Zero)
            {
                targetFocus = guiInfo.hwndFocus;
            }

            var sbClass = new StringBuilder(256);
            KeyboardNativeMethods.GetClassName(targetFocus, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();

            string processName = "Unknown";
            try
            {
                if (fgPid != 0)
                {
                    using (var proc = System.Diagnostics.Process.GetProcessById((int)fgPid))
                    {
                        processName = proc.ProcessName;
                    }
                }
            }
            catch { }

            if (_explorerTracker.ActiveInlineAdapter == null)
            {
                var matched = SwiftList.PluginSdk.InlineSearchAdapterRegistry.GetMatchingAdapter(targetFocus, className, processName);
                if (matched != null)
                {
                    _explorerTracker.SetActiveInlineAdapterDirectly(matched, targetFocus);
                }
            }

            bool isAdapterActive = _explorerTracker.ActiveInlineAdapter != null;
            if (IsInlineSearchVisible || isAdapterActive)
            {
                if (!IsInlineSearchVisible && isAdapterActive)
                {
                    bool canTrigger = _explorerTracker.ActiveInlineAdapter!.CanTrigger(targetFocus, className);
                    if (!canTrigger)
                    {
                        return false;
                    }
                }

                bool isIndexModifierDown = KeyboardUtils.CheckModifiersMatchOnly(_settings.SelectIndexModifier);
                if (isIndexModifierDown && IsInlineSearchVisible)
                {
                    int num = -1;
                    if (vkCode >= 0x31 && vkCode <= 0x39)
                        num = vkCode - 0x31 + 1;
                    else if (vkCode >= 0x61 && vkCode <= 0x69)
                        num = vkCode - 0x61 + 1;

                    if (num >= 1 && num <= 9)
                    {
                        OnCtrlNumberPressed?.Invoke(num);
                        return true; // Consume
                    }
                }

                bool ctrlDown = (KeyboardNativeMethods.GetKeyState(0x11) & 0x8000) != 0;
                bool altDown = (KeyboardNativeMethods.GetKeyState(0x12) & 0x8000) != 0;
                bool winDown = (KeyboardNativeMethods.GetKeyState(0x5B) & 0x8000) != 0 || 
                               (KeyboardNativeMethods.GetKeyState(0x5C) & 0x8000) != 0;
                if (ctrlDown || altDown || winDown)
                {
                    return false;
                }

                if (vkCode == KeyboardNativeMethods.VK_ESCAPE)
                {
                    if (IsInlineSearchVisible)
                    {
                        OnEscapePressed?.Invoke();
                        return true;
                    }
                    return false;
                }

                if (vkCode == KeyboardNativeMethods.VK_BACK && IsInlineSearchVisible)
                {
                    OnBackspacePressed?.Invoke();
                    return true;
                }

                if (vkCode == KeyboardNativeMethods.VK_RETURN && IsInlineSearchVisible)
                {
                    OnEnterPressed?.Invoke();
                    return true;
                }

                if (vkCode == KeyboardNativeMethods.VK_UP && IsInlineSearchVisible)
                {
                    OnUpPressed?.Invoke();
                    return true;
                }

                if (vkCode == KeyboardNativeMethods.VK_DOWN && IsInlineSearchVisible)
                {
                    OnDownPressed?.Invoke();
                    return true;
                }

                if (vkCode == KeyboardNativeMethods.VK_LEFT && IsInlineSearchVisible)
                {
                    OnLeftPressed?.Invoke();
                    return true;
                }

                if (vkCode == KeyboardNativeMethods.VK_RIGHT && IsInlineSearchVisible)
                {
                    OnRightPressed?.Invoke();
                    return true;
                }

                if (vkCode == KeyboardNativeMethods.VK_TAB)
                {
                    return false;
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
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
