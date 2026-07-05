using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SwiftList.App.Helpers;

public static class WpfUiHelper
{
    public static ModifierKeys GetWpfModifier(string modifierStr)
    {
        if (string.IsNullOrEmpty(modifierStr)) return ModifierKeys.Control;
        return modifierStr.Trim().ToUpperInvariant() switch
        {
            "ALT" => ModifierKeys.Alt,
            "SHIFT" => ModifierKeys.Shift,
            "WIN" or "WINDOWS" => ModifierKeys.Windows,
            "NONE" => ModifierKeys.None,
            _ => ModifierKeys.Control,
        };
    }

    /// <summary>When Alt is held, WPF sets e.Key = Key.System and e.SystemKey = the real key. Normalize it.</summary>
    public static Key GetActualKey(System.Windows.Input.KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

    /// <summary>Parses a recorder-style combo string (e.g. "Ctrl+Shift+Enter") into its key + modifiers.</summary>
    public static bool TryParseHotkey(string? hotkey, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;
        if (string.IsNullOrWhiteSpace(hotkey)) return false;

        foreach (var part in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = part.Trim().ToUpperInvariant();
            switch (clean)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "ALT":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "SHIFT":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (!Enum.TryParse(clean, true, out key) && clean.Length == 1 && char.IsDigit(clean[0]))
                        Enum.TryParse("D" + clean, true, out key);
                    break;
            }
        }

        return key != Key.None;
    }

    /// <summary>Whether the currently-held modifiers + key match a stored recorder-style combo string.</summary>
    public static bool MatchesHotkey(string? hotkey, ModifierKeys currentModifiers, Key currentKey) =>
        TryParseHotkey(hotkey, out var key, out var modifiers) && key == currentKey && modifiers == currentModifiers;


    public static ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}
