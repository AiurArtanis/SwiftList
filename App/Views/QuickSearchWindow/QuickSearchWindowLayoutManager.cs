using System.Windows;
using System.Windows.Threading;
using SwiftList.App.Helpers;
using SwiftList.App.Services;

namespace SwiftList.App;

/// <summary>
/// Manages panel-height layout math for QuickSearchWindow -- actions list height and results list
/// height. Ctrl+N shortcut-hint labeling is a separate concern handled by QuickSearchShortcutHelper
/// (still triggered from here after a resize, since that's when rows scroll into/out of view).
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
                    // A flat pixel ceiling, not a "cap at item #9" rule: the actions list has no reason
                    // to match the results list's row COUNT, only the same overall height BUDGET it tops
                    // out at, so a full-length actions list with several half-height separators can still
                    // show more than 9 rows as long as the total stays within that same pixel ceiling.
                    // ScaledNormalRowHeight (not ScaledSearchResultItemHeight) matches what the results
                    // list's own rows actually render at once the icon-size floor kicks in -- using the
                    // unfloored value here would cap the actions panel shorter than the results list's own
                    // 9-row ceiling. Reduced by actionsHeaderHeight (the panel's own top banner, the target
                    // filename) so a full actions list's total height (banner + rows) still tops out at
                    // that same budget -- otherwise the window visibly grows taller the moment actions
                    // mode's own banner is added on top of a full-height list.
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

    public void UpdateShortcutHints() =>
        QuickSearchShortcutHelper.UpdateShortcutHints(_window, WpfUiHelper.GetScrollViewer(_window.LstResults));
}
