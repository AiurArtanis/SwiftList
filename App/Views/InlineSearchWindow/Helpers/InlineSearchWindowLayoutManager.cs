using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

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
            double resultsHeight = 0;
            var foundSelectable = 0;
            var headerCount = 0;
            for (var i = 0; i < count; i++)
            {
                var item = _window.ViewModel.Results[i];
                // Must match CountSelectableResults' definition of "selectable", not the looser
                // !IsEmptyResult && !IsSearchSectionHeader check this used to have: that looser check
                // let the "show more"/jump-to-explorer row count as one of the 9, so depending on
                // exactly which row landed on the 9th slot, the sum either stopped one row short of
                // what's actually rendered or (via a stale 489px cap below, wide enough to never
                // clamp a merely-9-rows-tall list) let a taller-than-real sum through unclamped --
                // either way LstResults.Height stopped matching the real content height.
                var isSelectable = !item.IsEmptyResult && !item.IsSearchSectionHeader
                                    && item.FullPath != "__SHOW_MORE__" && !item.IsJumpToExplorerPath;
                if (isSelectable && foundSelectable == 9)
                    break;
                resultsHeight += GetItemHeight(item);
                if (isSelectable)
                    foundSelectable++;
                // A section header ("当前文件夹"/"全局搜索") is neither selectable nor skipped -- its
                // height still goes into resultsHeight above, but it doesn't count toward foundSelectable
                // reaching 9. maxAvailableHeight below needs to budget for it too, or a query that spans
                // two sections eats a whole selectable row's worth of height per header it shows,
                // clamping off real results the "current folder"-only or "global search"-only case never
                // loses (issue: two-section queries topped out at 8 visible rows, one-section at 9).
                else if (item.IsSearchSectionHeader)
                    headerCount++;
            }
            var pathPreviewHeight = 0.0;
            if (_window.PathPreviewBorder != null &&
                _window.PathPreviewBorder.Visibility == Visibility.Visible)
            {
                _window.PathPreviewBorder.Measure(new System.Windows.Size(_window.ResultsPanelControl.ActualWidth > 0 ? _window.ResultsPanelControl.ActualWidth : 437, double.PositiveInfinity));
                pathPreviewHeight = _window.PathPreviewBorder.DesiredSize.Height;
            }

            // Was a stale literal 489.0, left over from before inline rows were scaled to 0.7x --
            // UpdateActionsLayout below already derives its own "9 compact rows" cap from
            // SearchResultItemHeight (line ~103); this now matches it instead of allowing ~165px of
            // slack past what 9 real rows can ever actually sum to. Extended by headerCount rows (see
            // above) so a query with N section headers gets N extra rows' worth of budget for them, on
            // top of the 9 selectable rows they don't count against.
            var maxAvailableHeight = (9 + headerCount) * Math.Round(Services.UiMetrics.SearchResultItemHeight * 0.7) - pathPreviewHeight;
            var actualResultsHeight = Math.Max(0.0, Math.Min(resultsHeight, maxAvailableHeight));
            var totalResultsHeight = actualResultsHeight + pathPreviewHeight;
            var heightChanged = !AreClose(_lastResultsHeight, totalResultsHeight);
            if (heightChanged)
            {
                _lastResultsHeight = totalResultsHeight;
                _window.LstResults.Height = actualResultsHeight;
                _window.ResultsPanelControl.Height = actualResultsHeight;
            }

            if (count == 0)
            {
                _window.LstResults.SelectedIndex = -1;
            }

            UpdateShortcutHints();
            if (heightChanged)
                _window.Positioner.PositionWindow();
        }), DispatcherPriority.Background);
    }

    public void UpdateActionsLayout()
    {
        if (_window.ResultsPanelControl.ActionsGrid.Visibility == Visibility.Visible)
        {
            _window.PathPreviewBorder?.Visibility = Visibility.Collapsed;

            if (_window.LstActions.ItemsSource is System.Collections.IList items)
            {
                double totalHeight = 0;
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i] is ActionMenuItem item)
                    {
                        totalHeight += item.ItemHeight;
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
                    actualActionsHeight = 40;
                }
                else
                {
                    var maxAvailableHeight = 9 * Math.Round(Services.UiMetrics.SearchResultItemHeight * 0.7);
                    actualActionsHeight = Math.Max(0.0, Math.Min(totalHeight, maxAvailableHeight - actionsHeaderHeight));
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
            _lastResultsHeight = double.NaN;
            UpdatePathPreviewVisibility();
            QueueResultsLayoutUpdate();
        }

        _window.Positioner.PositionWindow();
    }

    public void UpdateShortcutHints()
    {
        var scrollViewer = GetScrollViewer(_window.LstResults);
        InlineSearchShortcutHelper.UpdateShortcutHints(_window, scrollViewer);
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
    private double GetItemHeight(AppSearchResult item) => item.InlineItemHeight;

    // Hovering a result now selects it (see ResultsControl.xaml.cs), so SelectedItem alone is already
    // the "active" result -- no separate hover-tracking state needed here anymore.
    public void UpdatePathPreviewVisibility() => _window.Dispatcher.BeginInvoke(new Action(() =>
                                                      {
                                                          if (_window.LstResults.SelectedItem is not AppSearchResult activeResult)
                                                          {
                                                              if (_window.PathPreviewBorder != null && _window.PathPreviewBorder.Visibility != Visibility.Collapsed)
                                                              {
                                                                  _window.PathPreviewBorder.Visibility = Visibility.Collapsed;
                                                                  QueueResultsLayoutUpdate();
                                                              }
                                                              return;
                                                          }

                                                          var isTruncated = CheckIfResultIsTruncated(activeResult);
                                                          var vm = _window.ViewModel;

                                                          var isShowMore = activeResult.FullPath == "__SHOW_MORE__";

                                                          var shouldShow = _window.ResultsPanelControl.ActionsGrid.Visibility != Visibility.Visible &&
                                                                            isTruncated &&
                                                                            vm.IsInlineSearchContext &&
                                                                            !activeResult.IsEmptyResult &&
                                                                            !activeResult.IsSearchSectionHeader &&
                                                                            !activeResult.IsListItem &&
                                                                            !activeResult.IsPluginSearchAction &&
                                                                            !activeResult.IsInstantResult &&
                                                                            (!string.IsNullOrEmpty(activeResult.FullPath) || isShowMore);

                                                          var targetVisibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
                                                          if (_window.PathPreviewBorder != null)
                                                          {
                                                              if (shouldShow)
                                                              {
                                                                  _window.PathPreviewTextBlock.Text = isShowMore ? activeResult.Name : ViewModels.Search.SearchResultHelper.FormatWslPath(activeResult.FullPath);
                                                              }

                                                              if (_window.PathPreviewBorder.Visibility != targetVisibility)
                                                              {
                                                                  _window.PathPreviewBorder.Visibility = targetVisibility;
                                                                  QueueResultsLayoutUpdate();
                                                              }
                                                          }
                                                      }), DispatcherPriority.Loaded);

    private bool CheckIfResultIsTruncated(AppSearchResult result)
    {
        if (_window.LstResults.ItemContainerGenerator.ContainerFromItem(result) is not ListBoxItem container) return false;

        var scrollViewers = new List<ScrollViewer>();
        FindScrollViewers(container, scrollViewers);
        foreach (var sv in scrollViewers)
        {
            if (sv.ScrollableWidth > 0)
            {
                if (result.IsJumpToExplorerPath && Grid.GetColumn(sv) == 1)
                {
                    continue;
                }
                return true;
            }
        }
        return false;
    }

    private static void FindScrollViewers(DependencyObject depObj, List<ScrollViewer> list)
    {
        if (depObj == null) return;
        if (depObj is ScrollViewer viewer)
        {
            list.Add(viewer);
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            FindScrollViewers(child, list);
        }
    }
}
