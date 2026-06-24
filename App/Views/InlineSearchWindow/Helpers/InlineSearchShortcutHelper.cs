using System.Windows;
using System.Windows.Controls;
using SwiftList.Core;

namespace SwiftList.App.Views.InlineSearchWindow.Helpers;

public static class InlineSearchShortcutHelper
{
    public static void UpdateShortcutHints(SwiftList.App.InlineSearchWindow window, ScrollViewer? scrollViewer)
    {
        var firstVisible = scrollViewer != null ? (int)Math.Round(scrollViewer.VerticalOffset) : 0;
        var shortcutIndex = 1;

        var selectMod = "Ctrl";
        var quickSwitchHint = "Ctrl+G";
        try
        {
            var settings = UserSettings.Load();
            var mod = settings.SelectIndexModifier;
            if (!string.IsNullOrEmpty(mod))
            {
                selectMod = string.Equals(mod, "Control", StringComparison.OrdinalIgnoreCase) ? "Ctrl" : mod;
            }

            var quickSwitch = settings.QuickSwitchHotkey;
            if (quickSwitch != null)
            {
                if (string.Equals(quickSwitch.Type, "KeyCombo", StringComparison.OrdinalIgnoreCase))
                {
                    var qsMod = quickSwitch.Modifier;
                    if (string.Equals(qsMod, "Control", StringComparison.OrdinalIgnoreCase)) qsMod = "Ctrl";

                    var qsKey = quickSwitch.Key;
                    if (string.Equals(qsKey, "Space", StringComparison.OrdinalIgnoreCase)) qsKey = "Space";
                    else if (string.Equals(qsKey, "Enter", StringComparison.OrdinalIgnoreCase)) qsKey = "Enter";
                    else if (string.Equals(qsKey, "Escape", StringComparison.OrdinalIgnoreCase)) qsKey = "Esc";
                    else if (string.Equals(qsKey, "Tab", StringComparison.OrdinalIgnoreCase)) qsKey = "Tab";

                    quickSwitchHint = string.Equals(qsMod, "None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(qsMod)
                        ? qsKey : $"{qsMod}+{qsKey}";
                }
                else
                {
                    var qsClickMod = quickSwitch.ClickModifier;
                    if (string.Equals(qsClickMod, "Control", StringComparison.OrdinalIgnoreCase)) qsClickMod = "Ctrl";
                    quickSwitchHint = $"{qsClickMod} x{quickSwitch.ClickCount}";
                }
            }
        }
        catch { }

        for (var i = 0; i < window.LstResults.Items.Count; i++)
        {
            if (window.LstResults.Items[i] is AppSearchResult item)
            {
                if (item.IsEmptyResult || item.IsSearchSectionHeader)
                {
                    item.ShortcutHint = string.Empty;
                    item.ShortcutVisibility = Visibility.Collapsed;
                    continue;
                }

                if (item.IsJumpToExplorerPath)
                {
                    item.ShortcutHint = quickSwitchHint;
                    item.ShortcutVisibility = Visibility.Visible;
                    continue;
                }

                if (i >= firstVisible && shortcutIndex <= 9)
                {
                    item.ShortcutHint = $"{selectMod}+{shortcutIndex}";
                    item.ShortcutVisibility = Visibility.Visible;
                    shortcutIndex++;
                }
                else
                {
                    item.ShortcutHint = string.Empty;
                    item.ShortcutVisibility = Visibility.Collapsed;
                }
            }
        }
    }
}
