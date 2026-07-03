using System.Windows.Input;
using SwiftList.App.Services;
using SwiftList.PluginSdk.Abstractions;
namespace SwiftList.App.Helpers;

public static class HotkeyActionTrigger
{
    public static bool TryExecute(System.Windows.Input.KeyEventArgs e, AppSearchResult result, ISearchWindow window)
    {
        if (result == null || window == null) return false;

        // Act on the whole selection (the full window supports multi-select); fall back to the
        // single active result for the quick/inline windows.
        var selection = new List<AppSearchResult>();
        try
        {
            foreach (var obj in window.LstResults.SelectedItems)
                if (obj is AppSearchResult r) selection.Add(r);
        }
        catch { }
        if (selection.Count == 0) selection.Add(result);
        var single = selection;

        var key = WpfUiHelper.GetActualKey(e);
        var modifiers = Keyboard.Modifiers;

        var windowType = GetWindowType(window);

        foreach (var registration in PluginManager.Instance.Actions)
        {
            var action = registration.Action;
            if (string.IsNullOrWhiteSpace(action.Hotkey))
                continue;

            if (ParseHotkey(action.Hotkey, out var hotkeyKey, out var hotkeyMods))
            {
                if (key == hotkeyKey && modifiers == hotkeyMods)
                {
                    if (action.IsVisibleInMenu(single, windowType) && action.CanExecute(single))
                    {
                        action.Execute(single, window);
                        return true;
                    }
                }
            }
        }

        // Also check dynamic providers (e.g. CustomActions plugin)
        foreach (var provider in PluginManager.Instance.DynamicProviders)
        {
            foreach (var (hotkey, execute) in provider.GetHotkeyActions(single))
            {
                if (ParseHotkey(hotkey, out var hotkeyKey, out var hotkeyMods)
                    && key == hotkeyKey && modifiers == hotkeyMods)
                {
                    window.HideWindow();
                    execute();
                    return true;
                }
            }
        }

        return false;
    }

    private static SearchWindowType GetWindowType(ISearchWindow window)
    {
        var name = window.GetType().Name;
        if (name == "InlineSearchWindow")
            return SearchWindowType.Inline;
        if (name == "QuickSearchWindow")
            return SearchWindowType.Quick;
        return SearchWindowType.Main;
    }

    private static bool ParseHotkey(string hotkey, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;

        if (string.IsNullOrWhiteSpace(hotkey))
            return false;

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var cleanPart = part.Trim().ToUpperInvariant();
            if (cleanPart == "CTRL" || cleanPart == "CONTROL")
            {
                modifiers |= ModifierKeys.Control;
            }
            else if (cleanPart == "ALT")
            {
                modifiers |= ModifierKeys.Alt;
            }
            else if (cleanPart == "SHIFT")
            {
                modifiers |= ModifierKeys.Shift;
            }
            else if (cleanPart == "WIN" || cleanPart == "WINDOWS")
            {
                modifiers |= ModifierKeys.Windows;
            }
            else
            {
                if (Enum.TryParse<Key>(cleanPart, true, out var parsedKey))
                {
                    key = parsedKey;
                }
                else if (cleanPart.Length == 1 && char.IsDigit(cleanPart[0]))
                {
                    Enum.TryParse("D" + cleanPart, true, out key);
                }
            }
        }

        return key != Key.None;
    }
}
