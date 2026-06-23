using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftList.App.Services;
using SwiftList.Core;

namespace SwiftList.App.Views.InlineSearchWindow.Helpers;

public sealed class InlineSearchWindowLayoutManager
{
    private readonly SwiftList.App.InlineSearchWindow _window;
    private int _layoutUpdateQueued;
    private double _lastResultsHeight = double.NaN;

    public InlineSearchWindowLayoutManager(SwiftList.App.InlineSearchWindow window) => _window = window ?? throw new ArgumentNullException(nameof(window));

    public void QueueResultsLayoutUpdate()
    {
        if (Interlocked.Exchange(ref _layoutUpdateQueued, 1) == 1)
            return;

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _layoutUpdateQueued, 0);
            if (!_window.IsVisible) return;

            var count = _window.ViewModel.Results.Count;
            var selectableCount = CountSelectableResults();
            double resultsHeight = 0;
            var foundSelectable = 0;
            for (var i = 0; i < count; i++)
            {
                var item = _window.ViewModel.Results[i];
                resultsHeight += GetItemHeight(item);
                if (!item.IsEmptyResult && !item.IsSearchSectionHeader)
                {
                    foundSelectable++;
                    if (foundSelectable == 9)
                    {
                        break;
                    }
                }
            }
            var heightChanged = !AreClose(_lastResultsHeight, resultsHeight);
            if (heightChanged)
            {
                _lastResultsHeight = resultsHeight;
                _window.LstResults.Height = resultsHeight;
                _window.ResultsPanelControl.Height = resultsHeight;
            }

            _window.TxtStatusInfo.Text = count > 0 && selectableCount > 0
                ? string.Format(TranslationManager.Instance["Search_ResultsCount"], selectableCount)
                : string.Empty;

            if (count == 0)
            {
                _window.LstResults.SelectedIndex = -1;
            }

            UpdateShortcutHints();
            if (heightChanged)
                _window.Positioner.PositionWindow();
        }), DispatcherPriority.ContextIdle);
    }

    public void UpdateActionsLayout()
    {
        if (_window.ResultsPanelControl.ActionsGrid.Visibility == Visibility.Visible)
        {
            if (_window.LstActions.ItemsSource is System.Collections.IList items)
            {
                double totalHeight = 0;
                var limit = Math.Min(items.Count, 9);
                for (var i = 0; i < limit; i++)
                {
                    if (items[i] is ActionMenuItem item)
                    {
                        if (item.IsSeparator) totalHeight += UiMetrics.ActionSeparatorHeight;
                        else if (item.IsSectionHeader) totalHeight += UiMetrics.ActionSectionHeaderHeight;
                        else totalHeight += UiMetrics.ActionItemHeight;
                    }
                }

                _window.LstActions.Height = totalHeight;
                _window.ResultsPanelControl.Height = totalHeight + UiMetrics.ActionsHeaderHeight;
            }
            else
            {
                _window.ResultsPanelControl.Height = 0;
            }
        }
        else
        {
            _window.LstActions.Height = double.NaN;
            _lastResultsHeight = double.NaN;
            QueueResultsLayoutUpdate();
        }

        _window.Positioner.PositionWindow();
    }

    public void UpdateShortcutHints()
    {
        var scrollViewer = GetScrollViewer(_window.LstResults);
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
                else // ModifierClick
                {
                    var qsClickMod = quickSwitch.ClickModifier;
                    if (string.Equals(qsClickMod, "Control", StringComparison.OrdinalIgnoreCase)) qsClickMod = "Ctrl";
                    quickSwitchHint = $"{qsClickMod} x{quickSwitch.ClickCount}";
                }
            }
        }
        catch { }

        for (var i = 0; i < _window.LstResults.Items.Count; i++)
        {
            if (_window.LstResults.Items[i] is AppSearchResult item)
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

    public ScrollViewer? GetScrollViewer(DependencyObject depObj)
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

    private int CountSelectableResults()
    {
        var count = 0;
        foreach (var item in _window.ViewModel.Results)
        {
            if (!item.IsEmptyResult && !item.IsSearchSectionHeader && item.FullPath != "__SHOW_MORE__" && !item.IsJumpToExplorerPath)
                count++;
        }
        return count;
    }

    private static bool AreClose(double left, double right)
    {
        if (double.IsNaN(left) || double.IsNaN(right))
            return false;

        return Math.Abs(left - right) < 0.5;
    }

    public static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;

            if (child is FrameworkContentElement fce)
            {
                child = fce.Parent;
            }
            else
            {
                child = VisualTreeHelper.GetParent(child);
            }
        }
        return null;
    }
    private double GetItemHeight(AppSearchResult item)
    {
        if (item.IsSearchSectionHeader) return 31;
        if (item.IsListItem) return 34;
        return 51; // ponytail: match ResultItemStyle default height to prevent bottom clipping
    }
}
