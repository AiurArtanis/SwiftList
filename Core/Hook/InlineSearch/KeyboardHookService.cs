using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.Core.Hook.InlineSearch;

public class KeyboardHookService : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private KeyboardNativeMethods.LowLevelKeyboardProc? _proc;
    private readonly ExplorerTracker _explorerTracker;

    private UserSettings _settings = UserSettings.Load();
    private GlobalHotkeyDetector _hotkeyDetector;

    private bool _hasPendingContextMenuTrigger;
    private uint _lastContextMenuTriggerTime;
    private const uint ContextMenuGraceMs = 400;

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
    // Wired from MouseHookService.OnRightButtonDown, and also called directly below for the VK_APPS
    // (Menu) key. Windows needs real, non-zero time to build a context menu (shell extension
    // IContextMenu handlers, etc.), so either trigger immediately followed by a menu mnemonic keypress
    // can beat GUI_INMENUMODE becoming true -- this records the trigger so HandleInlineSearchKeys can
    // give it a short grace window regardless.
    public void NotifyRightButtonDown(uint time) => MarkPendingContextMenuTrigger(time);

    private void MarkPendingContextMenuTrigger(uint time)
    {
        _hasPendingContextMenuTrigger = true;
        _lastContextMenuTriggerTime = time;
    }
    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;
        _proc = HookCallback;
        var hMod = KeyboardNativeMethods.GetModuleHandle(null);
        _hookId = KeyboardNativeMethods.SetWindowsHookEx(KeyboardNativeMethods.WH_KEYBOARD_LL, _proc, hMod, 0);
        if (_hookId == IntPtr.Zero)
        {
            Logger.Log($"[KeyboardHookService] Failed to install keyboard hook! Error={Marshal.GetLastWin32Error()}", LogLevel.Error);
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
        if (nCode >= 0 && (wParam == (IntPtr)KeyboardNativeMethods.WM_KEYUP || wParam == (IntPtr)KeyboardNativeMethods.WM_SYSKEYUP))
        {
            var hookStruct = Marshal.PtrToStructure<KeyboardNativeMethods.KBDLLHOOKSTRUCT>(lParam);
            _hotkeyDetector.OnKeyUp((int)hookStruct.vkCode);
        }
        if (nCode >= 0 && (wParam == (IntPtr)KeyboardNativeMethods.WM_KEYDOWN || wParam == (IntPtr)KeyboardNativeMethods.WM_SYSKEYDOWN))
        {
            var hookStruct = Marshal.PtrToStructure<KeyboardNativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var vkCode = (int)hookStruct.vkCode;
            var time = hookStruct.time;

            // The physical Menu/context-menu key, and Shift+F10, both open a context menu just like a
            // right-click does, with the same real, non-zero construction delay -- covered
            // unconditionally here (not gated by shouldDisableAllHooks below), matching how the
            // independent mouse hook's own right-click detection is never gated either. F10 arrives as
            // WM_SYSKEYDOWN, which this outer condition already includes alongside WM_KEYDOWN.
            var isShiftF10 = vkCode == KeyboardNativeMethods.VK_F10
                && (KeyboardNativeMethods.GetKeyState(KeyboardNativeMethods.VK_SHIFT) & 0x8000) != 0;
            if (vkCode == KeyboardNativeMethods.VK_APPS || isShiftF10)
            {
                MarkPendingContextMenuTrigger(time);
            }

            // 1. Detect Toggle Window Hotkey
            // Fullscreen apps share the same gate as the process blacklist: it only suppresses the
            // quick-window toggle, Quick Switch, and inline-search invocation below, and still yields
            // to an active file dialog (IsActiveWindowDialog) same as the blacklist does. Key-up
            // tracking and Explorer-tracker bookkeeping run unconditionally either way.
            var isFullscreenBlocking = !_settings.Hotkeys.AllowHotkeysInFullscreen && FullscreenHelper.IsForegroundWindowFullScreen();
            var shouldDisableAllHooks = (IsHotkeysDisabledTemporarily || ForegroundProcessGate.IsForegroundProcessBlacklisted(_settings.BlacklistedProcesses) || isFullscreenBlocking)
                                         && !_explorerTracker.IsActiveWindowDialog;

            if (!shouldDisableAllHooks && _hotkeyDetector.CheckToggleWindowHotkey(vkCode, time, out var consumeToggleKey, OnDoubleCtrl))
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
            var fgHwnd = KeyboardNativeMethods.GetForegroundWindow();
            if (fgHwnd != IntPtr.Zero)
            {
                KeyboardNativeMethods.GetWindowThreadProcessId(fgHwnd, out var fgPid);
                if (fgPid == AppProcessId || fgPid == (uint)Environment.ProcessId)
                {
                    var sbClass = new StringBuilder(256);
                    ExplorerNativeHooks.GetClassName(fgHwnd, sbClass, sbClass.Capacity);
                    if (!sbClass.ToString().Equals("#32770", StringComparison.OrdinalIgnoreCase))
                    {
                        return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }
                }
                var rootFg = ExplorerNativeHooks.GetAncestor(fgHwnd, ExplorerNativeHooks.GA_ROOTOWNER);
                if (rootFg == IntPtr.Zero) rootFg = fgHwnd;
                if (_explorerTracker.ActiveHwnd != IntPtr.Zero && rootFg != _explorerTracker.ActiveHwnd)
                {
                    if (!IsDescendantOrOwned(_explorerTracker.ActiveHwnd, fgHwnd) && !IsImeWindow(fgHwnd))
                    {
                        // Re-derive state for whatever's actually foreground now (fgHwnd), not just wipe
                        // to "nothing is active" -- the tracker's own async WinEvent-based tracking can
                        // lag behind real foreground changes (e.g. a dialog re-gaining focus right after
                        // Explorer briefly lost it to nothing), and this runs synchronously on the very
                        // same keystroke Quick Switch (Ctrl+G) reads IsActiveWindowDialog from a few
                        // lines below -- blindly deactivating here could clear that flag to false right
                        // before Quick Switch's own check saw it, on the keystroke that was supposed to
                        // trigger it in the first place.
                        _explorerTracker.ReclassifyActiveWindow(fgHwnd);
                    }
                }
            }
            // 3. Detect and handle Quick Switch Hotkey
            if (!shouldDisableAllHooks && _hotkeyDetector.CheckAndHandleQuickSwitch(vkCode, time, out var consumeQuickSwitchKey))
            {
                if (consumeQuickSwitchKey)
                {
                    return (IntPtr)1;
                }
            }
            // If text input is focused, bypass
            if (fgHwnd != IntPtr.Zero && InputFocusEvaluator.IsForegroundTextInputFocused(fgHwnd))
            {
                return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            if (_explorerTracker.IsActiveWindowDialog)
            {
                return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            // 4. Handle Inline Search key events
            if (!shouldDisableAllHooks && HandleInlineSearchKeys(vkCode, hookStruct, fgHwnd))
            {
                return (IntPtr)1;
            }
        }
        return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
    private bool HandleInlineSearchKeys(int vkCode, KeyboardNativeMethods.KBDLLHOOKSTRUCT hookStruct, IntPtr fgHwnd)
    {
        // A synthesized key event (SendInput/keybd_event) from some other process -- e.g. a
        // third-party automation tool's own virtual-key hotkey scheme (reported: Quicker's Right-Ctrl
        // + number combo) -- was otherwise indistinguishable from the user's own typing, so it got
        // swallowed as inline-search input (or as a "jump to result N" shortcut, if it happened to
        // match SelectJumpModifier) instead of reaching whatever it was actually meant for.
        if ((hookStruct.flags & KeyboardNativeMethods.LLKHF_INJECTED) != 0)
        {
            return false;
        }

        var targetFocus = fgHwnd;
        var threadId = KeyboardNativeMethods.GetWindowThreadProcessId(fgHwnd, out var fgPid);
        var guiInfo = new KeyboardNativeMethods.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<KeyboardNativeMethods.GUITHREADINFO>()
        };
        var hasGuiInfo = KeyboardNativeMethods.GetGUIThreadInfo(threadId, ref guiInfo);

        // A context/system menu (right-click menu, title-bar menu, or a submenu of either) is
        // currently open. Explorer doesn't move keyboard focus to the menu HWND while it's up --
        // guiInfo.hwndFocus below still resolves to whatever control opened it -- so without this,
        // a menu mnemonic/accelerator keypress (e.g. "r" for Properties) got swallowed as the first
        // inline-search character instead of reaching the menu.
        const uint menuModeFlags = KeyboardNativeMethods.GUI_INMENUMODE
            | KeyboardNativeMethods.GUI_SYSTEMMENUMODE
            | KeyboardNativeMethods.GUI_POPUPMENUMODE;
        if (hasGuiInfo && (guiInfo.flags & menuModeFlags) != 0)
        {
            return false;
        }

        // The menu isn't confirmed open yet, but a right-click or Menu-key press landed very recently --
        // most likely the menu it opens just hasn't finished being built. Give it a short grace window
        // before treating a fast trigger-then-mnemonic as inline-search input. Compared using the raw
        // hook timestamps (both are the same GetTickCount-based clock) rather than wall-clock "now", so
        // our own processing latency never inflates or shrinks the measured gap.
        if (_hasPendingContextMenuTrigger)
        {
            var elapsedSinceTrigger = unchecked((int)hookStruct.time - (int)_lastContextMenuTriggerTime);
            if (elapsedSinceTrigger >= 0 && elapsedSinceTrigger <= ContextMenuGraceMs)
            {
                return false;
            }
            _hasPendingContextMenuTrigger = false;
        }

        if (hasGuiInfo && guiInfo.hwndFocus != IntPtr.Zero)
        {
            targetFocus = guiInfo.hwndFocus;
        }
        var sbClass = new StringBuilder(256);
        KeyboardNativeMethods.GetClassName(targetFocus, sbClass, sbClass.Capacity);
        var className = sbClass.ToString();

        var processName = ForegroundProcessGate.GetProcessNameWithoutExtension(fgPid);

        Logger.Log(string.Format("[KeyboardHookService] HandleInlineSearchKeys: targetFocus=0x{0:X}, className={1}, processName={2}", targetFocus.ToInt64(), className, processName), LogLevel.Debug);

        if (_explorerTracker.ActiveInlineAdapter == null)
        {
            var matched = PluginSdk.Registries.InlineSearchAdapterRegistry.GetMatchingAdapter(targetFocus, className, processName);
            if (matched != null)
            {
                _explorerTracker.SetActiveInlineAdapterDirectly(matched, targetFocus);
            }
        }
        var isAdapterActive = _explorerTracker.ActiveInlineAdapter != null;
        Logger.Log(string.Format("[KeyboardHookService] HandleInlineSearchKeys: isAdapterActive={0}, ActiveInlineAdapter={1}", isAdapterActive, _explorerTracker.ActiveInlineAdapter?.GetType().Name ?? "null"), LogLevel.Debug);
        if (IsInlineSearchVisible || isAdapterActive)
        {
            if (!IsInlineSearchVisible && isAdapterActive)
            {
                var canTrigger = _explorerTracker.ActiveInlineAdapter!.CanTrigger(targetFocus, className);
                Logger.Log(string.Format("[KeyboardHookService] HandleInlineSearchKeys: CanTrigger={0}", canTrigger), LogLevel.Debug);
                if (!canTrigger)
                {
                    return false;
                }
            }
            var isIndexModifierDown = !string.IsNullOrEmpty(_settings.Hotkeys.SelectJumpModifier)
                && KeyboardUtils.CheckModifiersMatchOnly(_settings.Hotkeys.SelectJumpModifier);
            if (isIndexModifierDown && IsInlineSearchVisible)
            {
                var num = -1;
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
            var ctrlDown = (KeyboardNativeMethods.GetKeyState(0x11) & 0x8000) != 0;
            var altDown = (KeyboardNativeMethods.GetKeyState(0x12) & 0x8000) != 0;
            var winDown = (KeyboardNativeMethods.GetKeyState(0x5B) & 0x8000) != 0 ||
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
            var isTriggerKey = (vkCode == KeyboardNativeMethods.VK_PROCESSKEY) ||
                                (vkCode >= 0x41 && vkCode <= 0x5A) ||
                                (vkCode >= 0x30 && vkCode <= 0x39) ||
                                (vkCode >= 0x60 && vkCode <= 0x69);

            if (isTriggerKey)
            {
                // When an IME is composing, ignore what/how many keys are pressed: just pop the (empty)
                // inline window and keep swallowing keys until focus is taken. Never let them through to
                // the host window (which would drive the system's default IME composition popup instead).
                var imeOn = vkCode == KeyboardNativeMethods.VK_PROCESSKEY || KeyboardUtils.IsImeActive(fgHwnd);
                if (imeOn)
                {
                    if (!IsInlineSearchVisible)
                    {
                        OnCharacterTyped?.Invoke('\0');
                    }
                    return true;
                }

                if (!IsInlineSearchVisible)
                {
                    // No IME: inject the first typed character as before; later keys go to the focused box.
                    var ch = KeyboardUtils.GetUnicodeChar(hookStruct);
                    OnCharacterTyped?.Invoke(ch);
                    return true;
                }
                return false;
            }
        }
        return false;
    }
    public void Dispose() => Stop();

    private bool IsDescendantOrOwned(IntPtr parent, IntPtr child)
    {
        if (parent == IntPtr.Zero || child == IntPtr.Zero) return false;
        if (parent == child) return true;

        var current = child;
        while (current != IntPtr.Zero)
        {
            if (current == parent) return true;
            var temp = ExplorerNativeHooks.GetParent(current);
            if (temp == IntPtr.Zero || temp == current) break;
            current = temp;
        }
        var rootOwner = ExplorerNativeHooks.GetAncestor(child, ExplorerNativeHooks.GA_ROOTOWNER);
        if (rootOwner == parent) return true;

        return false;
    }
    private bool IsImeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var sbClass = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
        var fgClass = sbClass.ToString();
        return fgClass.Contains("IME", StringComparison.OrdinalIgnoreCase) ||
               fgClass.Contains("Candidate", StringComparison.OrdinalIgnoreCase) ||
               fgClass.Contains("InputTip", StringComparison.OrdinalIgnoreCase) ||
               fgClass.Contains("InputSwitch", StringComparison.OrdinalIgnoreCase);
    }
}
