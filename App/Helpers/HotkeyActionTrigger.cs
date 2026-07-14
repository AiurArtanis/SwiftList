using System.Windows.Input;
using SwiftList.App.Services;
using SwiftList.App.Services.PluginManagerCore;
using SwiftList.Core;
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

        var windowType = GetWindowType(window);

        // The full window is a persistent search app, so it stays open after a hotkey action; the
        // quick/inline windows are dismiss-on-use launchers and hide first (also so an open/admin action
        // that blocks on a UAC prompt doesn't keep the window up until the target launches).
        return TryExecute(e, selection, window, windowType, hideOnRun: windowType != SearchWindowType.Main);
    }

    /// <summary>
    /// Runs the action whose registered hotkey matches the key event, on the given selection/view. Used
    /// both by the search windows and by the action flyout (quick-nav) so hotkeys work the same anywhere.
    /// </summary>
    public static bool TryExecute(System.Windows.Input.KeyEventArgs e, IReadOnlyList<AppSearchResult> selection, IPluginSearchWindow view, SearchWindowType windowType, bool hideOnRun)
    {
        if (selection == null || selection.Count == 0 || view == null) return false;

        var key = WpfUiHelper.GetActualKey(e);
        var modifiers = Keyboard.Modifiers;

        var pluginActionHotkeys = UserSettings.Load().Hotkeys.PluginActionHotkeys;

        foreach (var registration in PluginManager.Instance.Actions)
        {
            var action = registration.Action;
            var effectiveHotkey = action.Hotkey;
            // Matches the plugin ID convention used by PluginSettings: the DLL file name without its extension.
            var pluginId = System.IO.Path.GetFileNameWithoutExtension(ComponentFilter.GetDllName(registration.Plugin));
            if (pluginActionHotkeys.TryGetValue(pluginId, out var overrides)
                && overrides.TryGetValue(action.GetType().Name, out var overrideHotkey))
            {
                effectiveHotkey = overrideHotkey;
            }

            if (string.IsNullOrWhiteSpace(effectiveHotkey))
                continue;

            if (ParseHotkey(effectiveHotkey, out var hotkeyKey, out var hotkeyMods))
            {
                if (key == hotkeyKey && modifiers == hotkeyMods)
                {
                    if (action.IsVisibleInMenu(selection, windowType) && action.CanExecute(selection))
                    {
                        if (hideOnRun) view.HideWindow();
                        action.Execute(selection, view);
                        return true;
                    }
                }
            }
        }

        // Also check dynamic providers (e.g. CustomActions plugin)
        foreach (var provider in PluginManager.Instance.DynamicActionProviders)
        {
            foreach (var (hotkey, execute) in provider.GetHotkeyActions(selection))
            {
                if (ParseHotkey(hotkey, out var hotkeyKey, out var hotkeyMods)
                    && key == hotkeyKey && modifiers == hotkeyMods)
                {
                    if (hideOnRun) view.HideWindow();
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
