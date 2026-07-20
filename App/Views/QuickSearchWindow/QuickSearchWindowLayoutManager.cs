using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SwiftList.App.Services;
using SwiftList.Core;

namespace SwiftList.App;

/// <summary>
/// Manages UI layout computations for QuickSearchWindow — actions list height,
/// results list height, and Ctrl+N shortcut hint updates.
/// </summary>
internal sealed class QuickSearchWindowLayoutManager
{
    private readonly QuickSearchWindow _window;
    private int _layoutUpdateQueued;

    internal QuickSearchWindowLayoutManager(QuickSearchWindow window) => _window = window;

    public void UpdateActionsLayout()
    {
        if (_window.ResultsPanelControl.ActionsGrid.Visibility == Visibility.Visible)
        {
            if (_window.LstActions.ItemsSource is System.Collections.IList items)
            {
                double totalHeight = 0;
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i] is ActionMenuItem item)
                    {
                        totalHeight += item.ScaledItemHeight;
                    }
                }

                double actionsHeaderHeight = 28;
                if (_window.LstResults.SelectedItem is AppSearchResult selectedResult)
                {
                    actionsHeaderHeight = selectedResult.ActionsHeaderHeight;
                }

                double actualActionsHeight;
                if (items.Count == 0)
                {
                    // Collapse to a small neat height for "No Search Results"
                    actualActionsHeight = 40;
                }
                else
                {
                    // ScaledNormalRowHeight (not ScaledSearchResultItemHeight) matches what the results
                    // list's own rows actually render at once the icon-size floor kicks in -- using the
                    // unfloored value here would cap the actions panel shorter than 9 real result rows.
                    // Reduced by actionsHeaderHeight (the panel's own top banner, the target filename)
                    // so a full actions list's total height (banner + rows) still tops out at the same
                    // 9-row budget the results list uses -- otherwise the window visibly grows taller
                    // the moment actions mode's own banner is added on top of a full 9 rows.
                    var maxAvailableHeight = 9 * UiMetrics.ScaledNormalRowHeight - actionsHeaderHeight;
                    // Let the height naturally fit the items count (free size dynamic resize)
                    actualActionsHeight = Math.Max(0.0, Math.Min(totalHeight, maxAvailableHeight));
                }
                _window.LstActions.Height = double.NaN;
                _window.ResultsPanelControl.Height = actualActionsHeight + actionsHeaderHeight;
            }
            else
            {
                _window.LstActions.Height = 40;
                _window.ResultsPanelControl.Height = 40 + 28;
            }
        }
        else
        {
            _window.LstActions.Height = double.NaN;
            QueueResultsLayoutUpdate();
        }

        _window.SizeToContent = SizeToContent.Manual;
        _window.SizeToContent = SizeToContent.WidthAndHeight;
    }

    public void QueueResultsLayoutUpdate()
    {
        if (Interlocked.Exchange(ref _layoutUpdateQueued, 1) == 1)
            return;

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _layoutUpdateQueued, 0);

            // Sum each visible row's own height rather than assuming a uniform row size -- a section
            // header, the "show more" row, or a row whose icon forces it to grow past the base height
            // (see MinHeight in ListBox.xaml) would otherwise throw off a single-height-times-count guess,
            // leaving stray blank space (or clipping) at the bottom of the list.
            var results = _window.ViewModel.Results;
            var visibleCount = Math.Min(results.Count, 9);
            double resultsHeight = 0;
            for (var i = 0; i < visibleCount; i++)
            {
                resultsHeight += results[i].ScaledItemHeight;
            }
            _window.LstResults.Height = resultsHeight;
            _window.ResultsPanelControl.Height = resultsHeight;

            UpdateShortcutHints();
            _window.SizeToContent = SizeToContent.Manual;
            _window.SizeToContent = SizeToContent.WidthAndHeight;
        }), DispatcherPriority.ContextIdle);
    }

    public void UpdateShortcutHints()
    {
        var scrollViewer = GetScrollViewer(_window.LstResults);
        var firstVisible = scrollViewer != null ? (int)Math.Round(scrollViewer.VerticalOffset) : 0;
        var shortcutIndex = 1;

        var selectMod = UserSettings.Load().Hotkeys.SelectJumpModifier;

        for (var i = 0; i < _window.LstResults.Items.Count; i++)
        {
            if (_window.LstResults.Items[i] is AppSearchResult item)
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

    private static ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer viewer) return viewer;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}
