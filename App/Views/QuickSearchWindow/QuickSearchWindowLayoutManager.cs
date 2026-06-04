using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SwiftList.App.Services;
using SwiftList.PluginSdk;
using SwiftList.Core;

namespace SwiftList.App
{
    /// <summary>
    /// Manages UI layout computations for QuickSearchWindow — actions list height,
    /// results list height, and Ctrl+N shortcut hint updates.
    /// </summary>
    internal sealed class QuickSearchWindowLayoutManager
    {
        private readonly QuickSearchWindow _window;
        private int _layoutUpdateQueued;

        internal QuickSearchWindowLayoutManager(QuickSearchWindow window)
        {
            _window = window;
        }

        public void UpdateActionsLayout()
        {
            if (_window.ResultsPanelControl.ActionsGrid.Visibility == Visibility.Visible)
            {
                var items = _window.LstActions.ItemsSource as System.Collections.IList;
                if (items != null)
                {
                    double totalHeight = 0;
                    int limit = Math.Min(items.Count, 9);
                    for (int i = 0; i < limit; i++)
                    {
                        var item = items[i] as ActionMenuItem;
                        if (item != null)
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
                QueueResultsLayoutUpdate();
            }

            _window.SizeToContent = SizeToContent.Manual;
            _window.SizeToContent = SizeToContent.Height;
        }

        public void QueueResultsLayoutUpdate()
        {
            if (Interlocked.Exchange(ref _layoutUpdateQueued, 1) == 1)
                return;

            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _layoutUpdateQueued, 0);

                int count = _window.ViewModel.Results.Count;
                double resultsHeight = Math.Min(count, 9) * UiMetrics.SearchResultItemHeight;
                _window.LstResults.Height = resultsHeight;
                _window.ResultsPanelControl.Height = resultsHeight;

                UpdateShortcutHints();
                _window.SizeToContent = SizeToContent.Manual;
                _window.SizeToContent = SizeToContent.Height;
            }), DispatcherPriority.ContextIdle);
        }

        public void UpdateShortcutHints()
        {
            var scrollViewer = GetScrollViewer(_window.LstResults);
            int firstVisible = scrollViewer != null ? (int)Math.Round(scrollViewer.VerticalOffset) : 0;
            int shortcutIndex = 1;

            string selectMod = "Ctrl";
            try
            {
                var mod = UserSettings.Load().SelectIndexModifier;
                if (!string.IsNullOrEmpty(mod))
                {
                    selectMod = string.Equals(mod, "Control", StringComparison.OrdinalIgnoreCase) ? "Ctrl" : mod;
                }
            }
            catch { }

            for (int i = 0; i < _window.LstResults.Items.Count; i++)
            {
                if (_window.LstResults.Items[i] is AppSearchResult item)
                {
                    if (item.IsEmptyResult || item.IsSearchSectionHeader)
                    {
                        item.ShortcutHint = string.Empty;
                        item.ShortcutVisibility = Visibility.Collapsed;
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

        private static ScrollViewer? GetScrollViewer(DependencyObject depObj)
        {
            if (depObj is ScrollViewer viewer) return viewer;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
