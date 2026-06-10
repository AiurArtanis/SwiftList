namespace SwiftList.Core.Hook.InlineSearch;

public sealed class GlobalHotkeyDetector
{
    private readonly UserSettings _settings;
    private readonly ExplorerTracker _explorerTracker;

    private uint _lastModifierDownTime;
    private int _lastModifierVkCode;
    private int _modifierClickCount;

    private uint _lastQuickSwitchModifierTime;
    private int _lastQuickSwitchModifierVkCode;
    private int _quickSwitchModifierClickCount;

    public GlobalHotkeyDetector(UserSettings settings, ExplorerTracker explorerTracker)
    {
        _settings = settings;
        _explorerTracker = explorerTracker;
    }

    public bool CheckToggleWindowHotkey(int vkCode, uint time, out bool consumeKey, Action? onDoubleCtrl)
    {
        consumeKey = false;
        var triggered = false;
        if (_settings.ToggleWindowHotkey?.Type == "ModifierClick")
        {
            if (KeyboardUtils.IsModifierKey(vkCode, _settings.ToggleWindowHotkey.ClickModifier))
            {
                var elapsed = time - _lastModifierDownTime;
                if (vkCode == _lastModifierVkCode && elapsed > 100 && elapsed < 500)
                {
                    _modifierClickCount++;
                    if (_modifierClickCount >= _settings.ToggleWindowHotkey.ClickCount)
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
        else if (_settings.ToggleWindowHotkey?.Type == "KeyCombo")
        {
            var targetVk = KeyboardUtils.GetKeyVirtualCode(_settings.ToggleWindowHotkey.Key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (KeyboardUtils.CheckModifiersMatch(_settings.ToggleWindowHotkey.Modifier))
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
        if (_settings.QuickSwitchHotkey?.Type == "ModifierClick")
        {
            if (KeyboardUtils.IsModifierKey(vkCode, _settings.QuickSwitchHotkey.ClickModifier))
            {
                var elapsed = time - _lastQuickSwitchModifierTime;
                if (vkCode == _lastQuickSwitchModifierVkCode && elapsed > 100 && elapsed < 500)
                {
                    _quickSwitchModifierClickCount++;
                    if (_quickSwitchModifierClickCount >= _settings.QuickSwitchHotkey.ClickCount)
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
        else if (_settings.QuickSwitchHotkey?.Type == "KeyCombo")
        {
            var targetVk = KeyboardUtils.GetKeyVirtualCode(_settings.QuickSwitchHotkey.Key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (KeyboardUtils.CheckModifiersMatch(_settings.QuickSwitchHotkey.Modifier))
                {
                    triggered = true;
                }
            }
        }

        if (_explorerTracker.IsActiveWindowDialog && triggered && _explorerTracker.ActiveAdapter != null)
        {
            var lastExplorerPath = _explorerTracker.LastActiveExplorerPath;
            if (!string.IsNullOrEmpty(lastExplorerPath) && Directory.Exists(lastExplorerPath))
            {
                var navPath = lastExplorerPath.EndsWith("\\") ? lastExplorerPath : lastExplorerPath + "\\";
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
