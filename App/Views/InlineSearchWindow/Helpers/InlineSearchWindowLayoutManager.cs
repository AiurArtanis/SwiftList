using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftList.App.Views.InlineSearchWindow.Helpers;

public sealed class InlineSearchWindowLayoutManager
{
    private readonly SwiftList.App.InlineSearchWindow _window;
    private int _layoutUpdateQueued;

    public InlineSearchWindowLayoutManager(SwiftList.App.InlineSearchWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));

        // LstResults is the same shared ResultsControl.xaml markup Quick/Inline/Full all use.
        // ScrollViewer.CanContentScroll="False" (pixel-based scrolling, so a 9-row budget that isn't a
        // whole multiple of the row height clips the boundary row instead of leaving the leftover
        // fraction as blank space -- see 78ddae91) used to be set right there in the shared XAML. Commit
        // 3f09b9bf removed it to fix the QUICK window's typing lag, replacing it with a per-pass dynamic
        // toggle scoped to that window's own layout manager -- but never gave this window an equivalent,
        // so LstResults here silently fell back to the WPF-default item-based virtualization and
        // reintroduced the exact blank-space bug 78ddae91 existed to fix. QueueResultsLayoutUpdate below
        // still Measure()s the real ListBox every update regardless of this setting, so unlike the quick
        // window there's no virtualization win to protect here in the first place -- fixing this
        // permanently, once, is free.
        ScrollViewer.SetCanContentScroll(_window.LstResults, false);
    }

    public void QueueResultsLayoutUpdate()
    {
        if (Interlocked.Exchange(ref _layoutUpdateQueued, 1) == 1)
            return;

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _layoutUpdateQueued, 0);
            if (!_window.IsVisible) return;

            // Every prior version of this (summing selectable rows only, giving headers their own extra
            // budget, lazily dropping a dangling header, matching the quick window's flat "first 9 items"
            // sum) was still a hand-computed PREDICTION of what WPF would render, kept in a separate
            // formula that had to stay perfectly in sync with the template/container styling by hand --
            // and every round, something about a header, a badge, or a banner made the two disagree by
            // exactly one row's worth. Measuring the real ListBox instead removes the prediction
            // entirely: there's no separate number to drift out of sync with, because this IS what WPF
            // is about to render.
            var count = _window.ViewModel.Results.Count;
            // PathPreviewBorder (the truncated-path banner above the list, Grid.Row sibling of
            // ResultsPanelControl -- see InlineSearchWindow.xaml) is never counted out of this 9-row
            // budget: unlike the quick window (which sizes itself via SizeToContent and so needs its
            // tab-strip case to land on the exact same total height as its bannerless/tabstrip-less case,
            // see QuickSearchWindowLayoutManager's own ceiling), this window's shell is a fixed 550px that
            // already has headroom for content to grow inside it (see InlineSearchWindowPositioner's own
            // comment on that) -- there's no bannerless sibling state it needs to visually match, so the
            // banner can simply add its own height on top of a full, uncompromised 9-row list.
            var maxAvailableHeight = 9 * Math.Round(Services.UiMetrics.SearchResultItemHeight * 0.7);

            var measureWidth = _window.ResultsPanelControl.ActualWidth > 0 ? _window.ResultsPanelControl.ActualWidth : 437;
            // A result-set change (e.g. ReconcileTo mutating item 0 in place and RemoveAt-ing the rest, see
            // SearchResultsReconciler) can leave the ListBox's own item-container generator not yet caught up
            // by the time this callback runs -- a directly-called Measure() below reuses whatever containers
            // the generator currently has, so measuring before that catches up could still count a container
            // that's about to be recycled away, adding a stray row's worth of height. Forcing a real layout
            // pass first (against the STILL-current Height, purely to flush any pending container generation)
            // guarantees the generator is caught up with ItemsSource before the actual measurement below
            // reads anything from it.
            _window.LstResults.InvalidateMeasure();
            _window.UpdateLayout();
            _window.LstResults.Height = double.NaN;
            // Measuring against maxAvailableHeight itself (rather than infinite/unconstrained height)
            // was the actual bug behind the persistent trailing gap, present regardless of the banner:
            // the ListBox's own template wraps its items in a ScrollViewer, and a ScrollViewer offered a
            // finite available height reports THAT height back as its desired size (it's a scrollable
            // container -- "I'll take whatever you give me and scroll internally" -- not "I'll shrink to
            // just what my content needs"), regardless of whether the actual item count fills it. So
            // DesiredSize.Height was silently just echoing maxAvailableHeight back on every call with
            // fewer than a full budget's worth of items, reserving a full 9-row-equivalent height no
            // matter how few rows were actually there. Measuring against infinity first forces the real,
            // content-driven size; the cap is then applied afterward, purely to bound a genuinely-long list.
            _window.LstResults.Measure(new System.Windows.Size(measureWidth, double.PositiveInfinity));
            var desiredHeight = _window.LstResults.DesiredSize.Height;

            var resultsHeight = Math.Min(desiredHeight, maxAvailableHeight);

            _window.LstResults.Height = resultsHeight;
            _window.ResultsPanelControl.Height = resultsHeight;
            // Forces layout to actually run right now, synchronously, instead of leaving WPF free to
            // repaint the ListBox with whatever's now bound to ItemsSource at its next opportunity
            // (which could win the race against this callback and render new content at the stale
            // Height briefly) -- mirrors what the quick window's own SizeToContent toggle achieves.
            _window.UpdateLayout();

            if (count == 0)
            {
                _window.LstResults.SelectedIndex = -1;
            }

            UpdateShortcutHints();
            _window.Positioner.PositionWindow();
        }), DispatcherPriority.Render);
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
                    // Not reduced by actionsHeaderHeight: that's the panel's own top banner (its target
                    // filename), additional content stacked above the action rows, not something sharing
                    // a fixed total budget with them -- see QueueResultsLayoutUpdate's own comment on the
                    // exact same fix for the results list's path-preview banner. Subtracting it here left
                    // the actions list unable to ever reach the same 9-row height the results list gets.
                    var maxAvailableHeight = 9 * Math.Round(Services.UiMetrics.SearchResultItemHeight * 0.7);
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
