using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SwiftList.App.Helpers
{
    public static class WpfUiHelper
    {
        public static ModifierKeys GetWpfModifier(string modifierStr)
        {
            if (string.IsNullOrEmpty(modifierStr)) return ModifierKeys.Control;
            switch (modifierStr.Trim().ToUpperInvariant())
            {
                case "ALT":
                    return ModifierKeys.Alt;
                case "SHIFT":
                    return ModifierKeys.Shift;
                case "WIN":
                case "WINDOWS":
                    return ModifierKeys.Windows;
                case "NONE":
                    return ModifierKeys.None;
                default:
                    return ModifierKeys.Control;
            }
        }

        public static ScrollViewer? GetScrollViewer(DependencyObject depObj)
        {
            if (depObj is ScrollViewer viewer) return viewer;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
