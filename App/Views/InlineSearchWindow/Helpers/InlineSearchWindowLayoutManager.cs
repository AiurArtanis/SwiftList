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
            var pathPreviewHeight = 0.0;
            if (_window.PathPreviewBorder != null && 
                _window.PathPreviewBorder.Visibility == Visibility.Visible)
            {
                _window.PathPreviewBorder.Measure(new System.Windows.Size(_window.ResultsPanelControl.ActualWidth > 0 ? _window.ResultsPanelControl.ActualWidth : 380, double.PositiveInfinity));
                pathPreviewHeight = _window.PathPreviewBorder.DesiredSize.Height;
            }

            var totalResultsHeight = resultsHeight + pathPreviewHeight;
            var heightChanged = !AreClose(_lastResultsHeight, totalResultsHeight);
            if (heightChanged)
            {
                _lastResultsHeight = totalResultsHeight;
                _window.LstResults.Height = resultsHeight;
                _window.ResultsPanelControl.Height = resultsHeight;
            }

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
            _window.PathPreviewBorder?.Visibility = Visibility.Collapsed;

            if (_window.LstActions.ItemsSource is System.Collections.IList items)
            {
                double totalHeight = 0;
                var limit = Math.Min(items.Count, 9);
                for (var i = 0; i < limit; i++)
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

                _window.LstActions.Height = totalHeight;
                _window.ResultsPanelControl.Height = totalHeight + actionsHeaderHeight;
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
    private double GetItemHeight(AppSearchResult item) => item.InlineItemHeight;

    private AppSearchResult? _hoveredResult;

    public void SetHoveredResult(AppSearchResult? result)
    {
        if (_hoveredResult != result)
        {
            _hoveredResult = result;
            UpdatePathPreviewVisibility();
        }
    }

    public void UpdatePathPreviewVisibility() => _window.Dispatcher.BeginInvoke(new Action(() =>
                                                      {
                                                          var activeResult = _hoveredResult ?? (_window.LstResults.SelectedItem as AppSearchResult);
                                                          if (activeResult == null)
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
                                                                            (!string.IsNullOrEmpty(activeResult.FullPath) || isShowMore);

                                                          var targetVisibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
                                                          if (_window.PathPreviewBorder != null)
                                                          {
                                                              if (shouldShow)
                                                              {
                                                                  _window.PathPreviewTextBlock.Text = isShowMore ? activeResult.Name : activeResult.FullPath;
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
