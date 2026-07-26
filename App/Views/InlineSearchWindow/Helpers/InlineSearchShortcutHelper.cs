using System.Windows;
using System.Windows.Controls;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;

namespace SwiftList.App.Views.InlineSearchWindow.Helpers;

public static class InlineSearchShortcutHelper
{
    public static void UpdateShortcutHints(SwiftList.App.InlineSearchWindow window, ScrollViewer? scrollViewer)
    {
        // LstResults here is pinned to pixel-based scrolling for the window's whole lifetime (see
        // InlineSearchWindowLayoutManager's constructor), unlike the quick window's per-pass dynamic
        // toggle -- reading it through the same mode-aware helper the quick window needs is one less
        // thing to keep in sync if that ever changes.
        var rowHeight = Math.Round(UiMetrics.SearchResultItemHeight * 0.7);
        var firstVisible = WpfUiHelper.GetFirstVisibleIndex(scrollViewer, rowHeight);
        var shortcutIndex = 1;

        var selectMod = "Ctrl";
        var quickSwitchHint = "Ctrl+G";
        try
        {
            var settings = UserSettings.Load().Hotkeys;
            selectMod = settings.SelectJumpModifier;

            var quickSwitch = settings.QuickSwitchHotkey;
            if (HotkeyStringFormat.IsBareModifier(quickSwitch, out var clickModifier))
            {
                var qsClickMod = string.Equals(clickModifier, "Control", StringComparison.OrdinalIgnoreCase) ? "Ctrl" : clickModifier;
                quickSwitchHint = $"{qsClickMod} x2";
            }
            else if (!string.IsNullOrEmpty(quickSwitch))
            {
                HotkeyStringFormat.ParseCombo(quickSwitch, out var qsMod, out var qsKey);
                if (string.Equals(qsMod, "Control", StringComparison.OrdinalIgnoreCase)) qsMod = "Ctrl";

                if (string.Equals(qsKey, "Escape", StringComparison.OrdinalIgnoreCase)) qsKey = "Esc";

                quickSwitchHint = string.IsNullOrEmpty(qsKey) ? string.Empty
                    : string.IsNullOrEmpty(qsMod) ? qsKey : $"{qsMod}+{qsKey}";
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
                    item.ShortcutVisibility = string.IsNullOrEmpty(quickSwitchHint) ? Visibility.Collapsed : Visibility.Visible;
                    continue;
                }

                if (!string.IsNullOrEmpty(selectMod) && i >= firstVisible && shortcutIndex <= 9)
                {
                    var prefix = string.Equals(selectMod, "None", StringComparison.OrdinalIgnoreCase) ? "" : $"{selectMod}+";
                    item.ShortcutHint = $"{prefix}{shortcutIndex}";
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
