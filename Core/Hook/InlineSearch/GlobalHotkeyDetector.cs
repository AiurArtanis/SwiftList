namespace SwiftList.Core.Hook.InlineSearch;

public sealed class GlobalHotkeyDetector
{
    private readonly UserSettings _settings;
    private readonly ExplorerTracker _explorerTracker;

    private uint _lastModifierDownTime;
    private int _lastModifierVkCode;
    private int _modifierClickCount;
    private bool _modifierWasReleased = true;

    private uint _lastQuickSwitchModifierTime;
    private int _lastQuickSwitchModifierVkCode;
    private int _quickSwitchModifierClickCount;
    private bool _quickSwitchModifierWasReleased = true;

    public GlobalHotkeyDetector(UserSettings settings, ExplorerTracker explorerTracker)
    {
        _settings = settings;
        _explorerTracker = explorerTracker;
    }

    private const int DoubleTapClickCount = 2;

    /// <summary>Call on WM_KEYUP / WM_SYSKEYUP to reset the "was released" flags.</summary>
    public void OnKeyUp(int vkCode)
    {
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.ToggleWindowHotkey, out var toggleModifier) &&
            KeyboardUtils.IsModifierKey(vkCode, toggleModifier))
        {
            _modifierWasReleased = true;
        }

        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.QuickSwitchHotkey, out var quickSwitchModifier) &&
            KeyboardUtils.IsModifierKey(vkCode, quickSwitchModifier))
        {
            _quickSwitchModifierWasReleased = true;
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
                // Key-repeat: the key was never released since last press — ignore
                if (!_modifierWasReleased)
                    return false;
                _modifierWasReleased = false;

                var elapsed = time - _lastModifierDownTime;
                if (vkCode == _lastModifierVkCode && elapsed > 100 && elapsed < 500)
                {
                    _modifierClickCount++;
                    if (_modifierClickCount >= DoubleTapClickCount)
                    {
                        _modifierClickCount = 0;
                        _lastModifierDownTime = 0;
                        _lastModifierVkCode = 0;
                        triggered = true;
                    }
                    else
                    {
                        _lastModifierDownTime = time;
                    }
                }
                else
                {
                    _modifierClickCount = 1;
                    _lastModifierDownTime = time;
                    _lastModifierVkCode = vkCode;
                }
            }
            else
            {
                _modifierClickCount = 0;
                _lastModifierDownTime = 0;
                _lastModifierVkCode = 0;
            }
        }
        else
        {
            HotkeyStringFormat.ParseCombo(_settings.Hotkeys.ToggleWindowHotkey, out var modifier, out var key);
            var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (KeyboardUtils.CheckModifiersMatch(modifier))
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
                // Key-repeat: the key was never released since last press — ignore
                if (!_quickSwitchModifierWasReleased)
                    return false;
                _quickSwitchModifierWasReleased = false;

                var elapsed = time - _lastQuickSwitchModifierTime;
                if (vkCode == _lastQuickSwitchModifierVkCode && elapsed > 100 && elapsed < 500)
                {
                    _quickSwitchModifierClickCount++;
                    if (_quickSwitchModifierClickCount >= DoubleTapClickCount)
                    {
                        _quickSwitchModifierClickCount = 0;
                        _lastQuickSwitchModifierTime = 0;
                        _lastQuickSwitchModifierVkCode = 0;
                        triggered = true;
                    }
                    else
                    {
                        _lastQuickSwitchModifierTime = time;
                    }
                }
                else
                {
                    _quickSwitchModifierClickCount = 1;
                    _lastQuickSwitchModifierTime = time;
                    _lastQuickSwitchModifierVkCode = vkCode;
                }
            }
            else
            {
                _quickSwitchModifierClickCount = 0;
                _lastQuickSwitchModifierTime = 0;
                _lastQuickSwitchModifierVkCode = 0;
            }
        }
        else
        {
            HotkeyStringFormat.ParseCombo(_settings.Hotkeys.QuickSwitchHotkey, out var modifier, out var key);
            var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (KeyboardUtils.CheckModifiersMatch(modifier))
                {
                    triggered = true;
                }
            }
        }

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
