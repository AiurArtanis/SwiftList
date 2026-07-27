namespace SwiftList.Core.Hook.InlineSearch;

public sealed class GlobalHotkeyDetector
{
    private readonly UserSettings _settings;
    private readonly ExplorerTracker _explorerTracker;

    private readonly ModifierDoubleTapDetector _toggleWindowTapDetector = new();
    private readonly ModifierDoubleTapDetector _quickSwitchTapDetector = new();
    private readonly WindowsKeyState _windowsKeyState = new();

    public GlobalHotkeyDetector(UserSettings settings, ExplorerTracker explorerTracker)
    {
        _settings = settings;
        _explorerTracker = explorerTracker;
    }

    public void OnKeyDown(int vkCode) => _windowsKeyState.OnKeyDown(vkCode);

    /// <summary>Call on WM_KEYUP / WM_SYSKEYUP to reset the "was released" flags.</summary>
    public void OnKeyUp(int vkCode)
    {
        _windowsKeyState.OnKeyUp(vkCode);
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.ToggleWindowHotkey, out var toggleModifier) &&
            KeyboardUtils.IsModifierKey(vkCode, toggleModifier))
        {
            _toggleWindowTapDetector.OnModifierKeyUp();
        }

        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.QuickSwitchHotkey, out var quickSwitchModifier) &&
            KeyboardUtils.IsModifierKey(vkCode, quickSwitchModifier))
        {
            _quickSwitchTapDetector.OnModifierKeyUp();
        }
    }

    public bool CheckToggleWindowHotkey(int vkCode, uint time, out bool consumeKey, Action? onDoubleCtrl)
    {
        consumeKey = false;
        var triggered = false;
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.ToggleWindowHotkey, out var clickModifier))
        {
            if (KeyboardUtils.IsModifierKey(vkCode, clickModifier))
            {
                triggered = _toggleWindowTapDetector.OnModifierKeyDown(vkCode, time);
            }
            else
            {
                _toggleWindowTapDetector.ResetOnOtherKey();
            }
        }
        else
        {
            HotkeyStringFormat.ParseCombo(_settings.Hotkeys.ToggleWindowHotkey, out var modifier, out var key);
            var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (KeyboardUtils.CheckModifiersMatch(modifier, _windowsKeyState.IsDown))
                {
                    triggered = true;
                    consumeKey = true;
                }
            }
        }

        if (triggered)
        {
            onDoubleCtrl?.Invoke();
        }
        return triggered;
    }

    public bool CheckAndHandleQuickSwitch(int vkCode, uint time, out bool consumeKey)
    {
        consumeKey = false;
        var triggered = false;
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.QuickSwitchHotkey, out var clickModifier))
        {
            if (KeyboardUtils.IsModifierKey(vkCode, clickModifier))
            {
                triggered = _quickSwitchTapDetector.OnModifierKeyDown(vkCode, time);
            }
            else
            {
                _quickSwitchTapDetector.ResetOnOtherKey();
            }
        }
        else
        {
            HotkeyStringFormat.ParseCombo(_settings.Hotkeys.QuickSwitchHotkey, out var modifier, out var key);
            var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (KeyboardUtils.CheckModifiersMatch(modifier, _windowsKeyState.IsDown))
                {
                    triggered = true;
                }
            }
        }

        return TryHandleQuickSwitchNavigation(triggered, out consumeKey);
    }

    // Quick Switch's trigger doesn't just toggle a window like the other hotkey does -- it re-navigates
    // the active (dialog) Explorer-like window back to the last folder that was active outside it. Kept as
    // its own method so the gesture-detection above (shared via ModifierDoubleTapDetector) and this
    // navigation policy read as two separate steps, even though they still live in the same class.
    private bool TryHandleQuickSwitchNavigation(bool triggered, out bool consumeKey)
    {
        consumeKey = false;
        if (_explorerTracker.IsActiveWindowDialog && triggered && _explorerTracker.ActiveAdapter != null)
        {
            var lastExplorerPath = _explorerTracker.LastActiveExplorerPath;
            var isValid = !string.IsNullOrEmpty(lastExplorerPath) &&
                          (Directory.Exists(lastExplorerPath) ||
                           (lastExplorerPath.Length >= 3 && lastExplorerPath[1] == ':' && lastExplorerPath[2] == '\\' && char.IsLetter(lastExplorerPath[0])));

            if (isValid)
            {
                var navPath = lastExplorerPath!.EndsWith("\\") ? lastExplorerPath : lastExplorerPath + "\\";
                var adapter = _explorerTracker.ActiveAdapter;
                var hwnd = _explorerTracker.ActiveHwnd;
                ThreadPool.QueueUserWorkItem(_ => adapter.NavigateTo(hwnd, navPath));
                consumeKey = true;
                return true;
            }
        }
        return false;
    }
}
