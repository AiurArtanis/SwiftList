using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.App.Services.PluginManagerCore;
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
        _toggleHotkeyValue = hotkeys.ToggleWindowHotkey;
        _quickSwitchHotkeyValue = hotkeys.QuickSwitchHotkey;

        _quickNavTriggerOnDoubleClick = hotkeys.QuickNavTriggerOnDoubleClick;
        _quickNavTriggerOnMiddleClick = hotkeys.QuickNavTriggerOnMiddleClick;

        _selectJumpModifier = hotkeys.SelectJumpModifier;
        _nextItemHotkey = hotkeys.NextItemHotkey;
        _previousItemHotkey = hotkeys.PreviousItemHotkey;
        _actionsMenuHotkey = hotkeys.ActionsMenuHotkey;
        _completeFromSelectionHotkey = hotkeys.CompleteFromSelectionHotkey;
        _quickLookHotkey = hotkeys.QuickLookHotkey;

        PluginActionGroups = BuildPluginActionGroups(hotkeys.PluginActionHotkeys);

        // Plugin action DisplayName/plugin Name are read live off the action/plugin objects, so they
        // need an explicit refresh on a runtime language switch (nothing else re-raises them).
        TranslationManager.Instance.PropertyChanged += (s, e) =>
        {
            foreach (var group in PluginActionGroups)
            {
                group.RefreshPluginName();
                foreach (var item in group.Items) item.RefreshDisplayName();
            }
        };
    }

    // Tab navigation
    private string _selectedTab = "Global";
    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    private ICommand? _selectTabCommand;
    public ICommand SelectTabCommand => _selectTabCommand ??= new RelayCommand<string>(tab => SelectedTab = tab);

    public List<PluginActionGroupViewModel> PluginActionGroups { get; }

    private static List<PluginActionGroupViewModel> BuildPluginActionGroups(Dictionary<string, Dictionary<string, string>> overrides)
    {
        var groups = new List<PluginActionGroupViewModel>();
        foreach (var pluginGroup in PluginManager.Instance.AllActions.GroupBy(r => r.Plugin))
        {
            // Matches the plugin ID convention already used by PluginSettings/PluginConfigFieldViewModel:
            // the DLL file name with its extension stripped (e.g. "SwiftList.Plugins.CoreExtensions").
            var pluginId = System.IO.Path.GetFileNameWithoutExtension(ComponentFilter.GetDllName(pluginGroup.Key));
            var items = pluginGroup.Select(reg =>
            {
                var currentValue = overrides.TryGetValue(pluginId, out var pluginOverrides)
                    && pluginOverrides.TryGetValue(reg.Action.Id, out var overrideValue)
                    ? overrideValue
                    : reg.Action.Hotkey;
                return new PluginActionHotkeyItemViewModel(pluginId, reg.Action, currentValue);
            }).ToList();

            if (items.Count > 0)
                groups.Add(new PluginActionGroupViewModel(pluginGroup.Key, items));
        }
        return groups;
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

    // Toggle Window hotkey: a single recorder value stored verbatim in the flat format described by
    // HotkeyStringFormat. A bare modifier (e.g. "Ctrl") means "double-tap this modifier"; a full combo
    // (e.g. "Alt+Space") means a literal key combination.
    private string _toggleHotkeyValue;
    public string ToggleHotkeyValue
    {
        get => _toggleHotkeyValue;
        set
        {
            if (SetProperty(ref _toggleHotkeyValue, value))
                OnPropertyChanged(nameof(IsToggleModifierClick));
        }
    }

    // Whether the toggle hotkey is currently a bare modifier (double-tap mode) -- drives the
    // "(Double Tap)" hint shown next to the recorder.
    public bool IsToggleModifierClick => HotkeyStringFormat.IsBareModifier(_toggleHotkeyValue, out _);

    // Quick Switch: same flat format, bound directly to the recorder (combo-only in the current UI).
    private string _quickSwitchHotkeyValue;
    public string QuickSwitchComboHotkey
    {
        get => _quickSwitchHotkeyValue;
        set => SetProperty(ref _quickSwitchHotkeyValue, value);
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

    public void Apply()
    {
        var hotkeys = _userSettings.Hotkeys;

        hotkeys.ToggleWindowHotkey = ToggleHotkeyValue;
        hotkeys.QuickSwitchHotkey = QuickSwitchComboHotkey;

        hotkeys.QuickNavTriggerOnDoubleClick = QuickNavTriggerOnDoubleClick;
        hotkeys.QuickNavTriggerOnMiddleClick = QuickNavTriggerOnMiddleClick;

        hotkeys.SelectJumpModifier = SelectJumpModifier;
        hotkeys.NextItemHotkey = NextItemHotkey;
        hotkeys.PreviousItemHotkey = PreviousItemHotkey;
        hotkeys.ActionsMenuHotkey = ActionsMenuHotkey;
        hotkeys.CompleteFromSelectionHotkey = CompleteFromSelectionHotkey;
        hotkeys.QuickLookHotkey = QuickLookHotkey;

        var pluginActionHotkeys = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in PluginActionGroups)
        {
            foreach (var item in group.Items)
            {
                if (item.HotkeyValue == item.DefaultHotkey) continue; // matches the built-in default -- no override needed
                if (!pluginActionHotkeys.TryGetValue(item.PluginId, out var pluginOverrides))
                {
                    pluginOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    pluginActionHotkeys[item.PluginId] = pluginOverrides;
                }
                pluginOverrides[item.ActionId] = item.HotkeyValue;
            }
        }
        hotkeys.PluginActionHotkeys = pluginActionHotkeys;

        _userSettings.Save();

        // Notify hook service process via IPC to reload settings!
        App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
    }
}
