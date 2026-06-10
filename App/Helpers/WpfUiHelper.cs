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
