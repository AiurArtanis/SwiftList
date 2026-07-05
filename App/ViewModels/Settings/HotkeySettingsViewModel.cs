using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

public class HotkeySettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public HotkeySettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        var hotkeys = _userSettings.Hotkeys;

        // Initialize local bindings from user settings
        _toggleType = hotkeys.ToggleWindowHotkey.Type;
        _toggleClickModifier = hotkeys.ToggleWindowHotkey.ClickModifier;
        _toggleClickCount = hotkeys.ToggleWindowHotkey.ClickCount;
        _toggleModifier = hotkeys.ToggleWindowHotkey.Modifier == "None" ? "Control" : hotkeys.ToggleWindowHotkey.Modifier;
        _toggleKey = hotkeys.ToggleWindowHotkey.Key;

        _quickSwitchModifier = hotkeys.QuickSwitchHotkey.Modifier == "None" ? "Control" : hotkeys.QuickSwitchHotkey.Modifier;
        _quickSwitchKey = hotkeys.QuickSwitchHotkey.Key;

        _quickNavTriggerOnDoubleClick = hotkeys.QuickNavTriggerOnDoubleClick;
        _quickNavTriggerOnMiddleClick = hotkeys.QuickNavTriggerOnMiddleClick;

        _selectJumpModifier = hotkeys.SelectJumpModifier;
        _nextItemHotkey = hotkeys.NextItemHotkey;
        _previousItemHotkey = hotkeys.PreviousItemHotkey;
        _actionsMenuHotkey = hotkeys.ActionsMenuHotkey;
        _completeFromSelectionHotkey = hotkeys.CompleteFromSelectionHotkey;
        _quickLookHotkey = hotkeys.QuickLookHotkey;
    }

    // Quick Navigation properties
    private bool _quickNavTriggerOnDoubleClick;
    public bool QuickNavTriggerOnDoubleClick
    {
        get => _quickNavTriggerOnDoubleClick;
        set => SetProperty(ref _quickNavTriggerOnDoubleClick, value);
    }

    private bool _quickNavTriggerOnMiddleClick;
    public bool QuickNavTriggerOnMiddleClick
    {
        get => _quickNavTriggerOnMiddleClick;
        set => SetProperty(ref _quickNavTriggerOnMiddleClick, value);
    }

    // Toggle Window hotkey: merged into a single recorder value. A bare modifier (e.g. "Ctrl") means
    // "double-tap this modifier"; a full combo (e.g. "Alt+Space") means a literal key combination.
    private string _toggleType;
    private string _toggleClickModifier;
    private int _toggleClickCount;
    private string _toggleModifier;
    private string _toggleKey;

    public string ToggleHotkeyValue
    {
        get => _toggleType == "ModifierClick"
            ? (_toggleClickModifier == "Control" ? "Ctrl" : _toggleClickModifier)
            : FormatComboHotkey(_toggleModifier, _toggleKey);
        set
        {
            if (ModifierTokens.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                _toggleType = "ModifierClick";
                _toggleClickModifier = value == "Ctrl" ? "Control" : value;
                _toggleClickCount = 2;
            }
            else
            {
                _toggleType = "KeyCombo";
                ParseComboHotkey(value, out _toggleModifier, out _toggleKey);
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsToggleModifierClick));
        }
    }

    // Whether the toggle hotkey is currently a bare modifier (double-tap mode) -- drives the
    // "(Double Tap)" hint shown next to the recorder.
    public bool IsToggleModifierClick => _toggleType == "ModifierClick";

    // Quick Switch properties (combo-key only)
    private string _quickSwitchModifier;
    public string QuickSwitchModifier
    {
        get => _quickSwitchModifier;
        set => SetProperty(ref _quickSwitchModifier, value);
    }

    private string _quickSwitchKey;
    public string QuickSwitchKey
    {
        get => _quickSwitchKey;
        set => SetProperty(ref _quickSwitchKey, value);
    }

    // Function key shortcuts, each stored directly in HotkeyRecorderControl's own combo format
    private string _selectJumpModifier;
    public string SelectJumpModifier
    {
        get => _selectJumpModifier;
        set => SetProperty(ref _selectJumpModifier, value);
    }

    private string _nextItemHotkey;
    public string NextItemHotkey
    {
        get => _nextItemHotkey;
        set => SetProperty(ref _nextItemHotkey, value);
    }

    private string _previousItemHotkey;
    public string PreviousItemHotkey
    {
        get => _previousItemHotkey;
        set => SetProperty(ref _previousItemHotkey, value);
    }

    private string _actionsMenuHotkey;
    public string ActionsMenuHotkey
    {
        get => _actionsMenuHotkey;
        set => SetProperty(ref _actionsMenuHotkey, value);
    }

    private string _completeFromSelectionHotkey;
    public string CompleteFromSelectionHotkey
    {
        get => _completeFromSelectionHotkey;
        set => SetProperty(ref _completeFromSelectionHotkey, value);
    }

    private string _quickLookHotkey;
    public string QuickLookHotkey
    {
        get => _quickLookHotkey;
        set => SetProperty(ref _quickLookHotkey, value);
    }

    // Composite hotkey string for HotkeyRecorderControl binding
    public string QuickSwitchComboHotkey
    {
        get => FormatComboHotkey(QuickSwitchModifier, QuickSwitchKey);
        set { ParseComboHotkey(value, out var mod, out var k); QuickSwitchModifier = mod; QuickSwitchKey = k; OnPropertyChanged(); }
    }

    private static readonly string[] ModifierTokens = { "Ctrl", "Alt", "Shift", "Win" };

    private static string FormatComboHotkey(string modifier, string key)
    {
        var mod = modifier == "Control" ? "Ctrl" : modifier;
        if (string.IsNullOrEmpty(key)) return string.IsNullOrEmpty(mod) ? string.Empty : mod;
        return string.IsNullOrEmpty(mod) ? key : $"{mod}+{key}";
    }

    private static void ParseComboHotkey(string value, out string modifier, out string key)
    {
        if (string.IsNullOrWhiteSpace(value)) { modifier = string.Empty; key = string.Empty; return; }
        var parts = value.Split('+');
        if (parts.Length == 1)
        {
            // A single token is either a bare modifier alone (e.g. "Ctrl") or a bare key with no
            // modifier (e.g. "P") -- tell them apart instead of always assuming the latter.
            if (ModifierTokens.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
            {
                modifier = parts[0] == "Ctrl" ? "Control" : parts[0];
                key = string.Empty;
            }
            else
            {
                modifier = string.Empty;
                key = parts[0];
            }
            return;
        }
        key = parts[^1];
        var modPart = parts[0];
        modifier = modPart == "Ctrl" ? "Control" : modPart; // Win/Alt/Shift pass through
    }

    public void Apply()
    {
        var hotkeys = _userSettings.Hotkeys;

        hotkeys.ToggleWindowHotkey = new HotkeySetting
        {
            Type = _toggleType,
            ClickModifier = _toggleClickModifier,
            ClickCount = _toggleClickCount,
            Modifier = _toggleModifier,
            Key = _toggleKey
        };

        hotkeys.QuickSwitchHotkey = new HotkeySetting
        {
            Type = "KeyCombo",
            Modifier = QuickSwitchModifier,
            Key = QuickSwitchKey
        };

        hotkeys.QuickNavTriggerOnDoubleClick = QuickNavTriggerOnDoubleClick;
        hotkeys.QuickNavTriggerOnMiddleClick = QuickNavTriggerOnMiddleClick;

        hotkeys.SelectJumpModifier = SelectJumpModifier;
        hotkeys.NextItemHotkey = NextItemHotkey;
        hotkeys.PreviousItemHotkey = PreviousItemHotkey;
        hotkeys.ActionsMenuHotkey = ActionsMenuHotkey;
        hotkeys.CompleteFromSelectionHotkey = CompleteFromSelectionHotkey;
        hotkeys.QuickLookHotkey = QuickLookHotkey;
        _userSettings.Save();

        // Notify hook service process via IPC to reload settings!
        App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
    }
}
