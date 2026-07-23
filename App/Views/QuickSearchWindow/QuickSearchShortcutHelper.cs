using System.Windows;
using System.Windows.Controls;
using SwiftList.Core;

namespace SwiftList.App;

// Computes and assigns the Ctrl+N-style shortcut-hint labels shown on each visible result row.
// Mirrors InlineSearchWindow's own InlineSearchShortcutHelper: kept separate from
// QuickSearchWindowLayoutManager, which owns panel-height layout math -- sizing a panel and labeling a
// row's keyboard shortcut are unrelated concerns that only happen to run back-to-back after a resize.
internal static class QuickSearchShortcutHelper
{
    public static void UpdateShortcutHints(QuickSearchWindow window, ScrollViewer? scrollViewer)
    {
        var firstVisible = scrollViewer != null ? (int)Math.Round(scrollViewer.VerticalOffset) : 0;
        var shortcutIndex = 1;

        var selectMod = UserSettings.Load().Hotkeys.SelectJumpModifier;

        for (var i = 0; i < window.LstResults.Items.Count; i++)
        {
            if (window.LstResults.Items[i] is AppSearchResult item)
            {
                if (item.IsEmptyResult || item.IsSearchSectionHeader || string.IsNullOrEmpty(selectMod))
                {
                    item.ShortcutHint = string.Empty;
                    item.ShortcutVisibility = Visibility.Collapsed;
                    continue;
                }

                if (i >= firstVisible && shortcutIndex <= 9)
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
