using SwiftList.App.Services;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

public class HotkeySettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public HotkeySettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        // Initialize local bindings from user settings
        _toggleType = _userSettings.ToggleWindowHotkey?.Type ?? "ModifierClick";
        _toggleClickModifier = _userSettings.ToggleWindowHotkey?.ClickModifier ?? "Control";
        _toggleClickCount = _userSettings.ToggleWindowHotkey?.ClickCount ?? 2;
        _toggleModifier = _userSettings.ToggleWindowHotkey?.Modifier ?? "Control";
        if (_toggleModifier == "None") _toggleModifier = "Control";
        _toggleKey = _userSettings.ToggleWindowHotkey?.Key ?? "Space";

        _quickSwitchType = _userSettings.QuickSwitchHotkey?.Type ?? "KeyCombo";
        _quickSwitchClickModifier = _userSettings.QuickSwitchHotkey?.ClickModifier ?? "Control";
        _quickSwitchClickCount = _userSettings.QuickSwitchHotkey?.ClickCount ?? 2;
        _quickSwitchModifier = _userSettings.QuickSwitchHotkey?.Modifier ?? "Control";
        if (_quickSwitchModifier == "None") _quickSwitchModifier = "Control";
        _quickSwitchKey = _userSettings.QuickSwitchHotkey?.Key ?? "G";

        _selectIndexModifier = _userSettings.SelectIndexModifier ?? "Control";
        _quickNavTriggerOnDoubleClick = _userSettings.QuickNavTriggerOnDoubleClick;
        _quickNavTriggerOnMiddleClick = _userSettings.QuickNavTriggerOnMiddleClick;

        _selectedToggleType = HotkeyTypeOptions.FirstOrDefault(o => o.Value.ToString() == _toggleType);
        _selectedQuickSwitchType = HotkeyTypeOptions.FirstOrDefault(o => o.Value.ToString() == _quickSwitchType);

        // Dynamically refresh properties when the language changes
        TranslationManager.Instance.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HotkeyTypeOptions));
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                SelectedToggleType = HotkeyTypeOptions.FirstOrDefault(o => o.Value.ToString() == ToggleType);
                SelectedQuickSwitchType = HotkeyTypeOptions.FirstOrDefault(o => o.Value.ToString() == QuickSwitchType);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        };
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

    // Toggle Window properties
    private string _toggleType;
    public string ToggleType
    {
        get => _toggleType;
        set
        {
            if (SetProperty(ref _toggleType, value))
            {
                OnPropertyChanged(nameof(IsToggleModifierClick));
                OnPropertyChanged(nameof(IsToggleKeyCombo));
            }
        }
    }
    public bool IsToggleModifierClick => ToggleType == "ModifierClick";
    public bool IsToggleKeyCombo => ToggleType == "KeyCombo";

    private HotkeyOptionItem? _selectedToggleType;
    public HotkeyOptionItem? SelectedToggleType
    {
        get => _selectedToggleType;
        set
        {
            if (value == null) return;
            if (SetProperty(ref _selectedToggleType, value))
            {
                ToggleType = value.Value.ToString() ?? "ModifierClick";
            }
        }
    }

    private string _toggleClickModifier;
    public string ToggleClickModifier
    {
        get => _toggleClickModifier;
        set => SetProperty(ref _toggleClickModifier, value);
    }

    private int _toggleClickCount;
    public int ToggleClickCount
    {
        get => _toggleClickCount;
        set => SetProperty(ref _toggleClickCount, value);
    }

    private string _toggleModifier;
    public string ToggleModifier
    {
        get => _toggleModifier;
        set => SetProperty(ref _toggleModifier, value);
    }

    private string _toggleKey;
    public string ToggleKey
    {
        get => _toggleKey;
        set => SetProperty(ref _toggleKey, value);
    }

    // Quick Switch properties
    private string _quickSwitchType;
    public string QuickSwitchType
    {
        get => _quickSwitchType;
        set
        {
            if (SetProperty(ref _quickSwitchType, value))
            {
                OnPropertyChanged(nameof(IsQuickSwitchModifierClick));
                OnPropertyChanged(nameof(IsQuickSwitchKeyCombo));
            }
        }
    }
    public bool IsQuickSwitchModifierClick => QuickSwitchType == "ModifierClick";
    public bool IsQuickSwitchKeyCombo => QuickSwitchType == "KeyCombo";

    private HotkeyOptionItem? _selectedQuickSwitchType;
    public HotkeyOptionItem? SelectedQuickSwitchType
    {
        get => _selectedQuickSwitchType;
        set
        {
            if (value == null) return;
            if (SetProperty(ref _selectedQuickSwitchType, value))
            {
                QuickSwitchType = value.Value.ToString() ?? "KeyCombo";
            }
        }
    }

    private string _quickSwitchClickModifier;
    public string QuickSwitchClickModifier
    {
        get => _quickSwitchClickModifier;
        set => SetProperty(ref _quickSwitchClickModifier, value);
    }

    private int _quickSwitchClickCount;
    public int QuickSwitchClickCount
    {
        get => _quickSwitchClickCount;
        set => SetProperty(ref _quickSwitchClickCount, value);
    }

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

    // Select Index (1-9) properties
    private string _selectIndexModifier;
    public string SelectIndexModifier
    {
        get => _selectIndexModifier;
        set => SetProperty(ref _selectIndexModifier, value);
    }

    // Options lists
    public List<HotkeyOptionItem> HotkeyTypeOptions => new()
    {
        new("ModifierClick", TranslationManager.Instance["Hotkeys_TypeClick"]),
        new("KeyCombo", TranslationManager.Instance["Hotkeys_TypeCombo"])
    };

    public List<HotkeyOptionItem> ModifierOptions =>
        new List<string> { "Control", "Alt", "Shift", "Win" }
        .Select(x => new HotkeyOptionItem(x, x)).ToList();

    public List<HotkeyOptionItem> ClickModifierOptions =>
        new List<string> { "Control", "Alt", "Shift", "Win" }
        .Select(x => new HotkeyOptionItem(x, x)).ToList();

    public List<HotkeyOptionItem> ClickCountOptions =>
        new List<int> { 1, 2, 3 }
        .Select(x => new HotkeyOptionItem(x, x.ToString())).ToList();

    public List<HotkeyOptionItem> SelectIndexModifierOptions =>
        new List<string> { "Control", "Alt", "Shift" }
        .Select(x => new HotkeyOptionItem(x, x)).ToList();

    public List<HotkeyOptionItem> KeyOptions =>
        new List<string>
        {
            "Space", "Tab", "Enter", "Escape",
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
            "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
        }
        .Select(x => new HotkeyOptionItem(x, x)).ToList();

    public void Apply()
    {
        _userSettings.ToggleWindowHotkey = new HotkeySetting
        {
            Type = ToggleType,
            ClickModifier = ToggleClickModifier,
            ClickCount = ToggleClickCount,
            Modifier = ToggleModifier,
            Key = ToggleKey
        };

        _userSettings.QuickSwitchHotkey = new HotkeySetting
        {
            Type = QuickSwitchType,
            ClickModifier = QuickSwitchClickModifier,
            ClickCount = QuickSwitchClickCount,
            Modifier = QuickSwitchModifier,
            Key = QuickSwitchKey
        };

        _userSettings.SelectIndexModifier = SelectIndexModifier;
        _userSettings.QuickNavTriggerOnDoubleClick = QuickNavTriggerOnDoubleClick;
        _userSettings.QuickNavTriggerOnMiddleClick = QuickNavTriggerOnMiddleClick;
        _userSettings.Save();

        // Notify hook service process via IPC to reload settings!
        App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
    }
}
