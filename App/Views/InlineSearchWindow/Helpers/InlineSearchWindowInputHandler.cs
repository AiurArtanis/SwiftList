using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SwiftList.App.Services;
using SwiftList.PluginSdk;
using SwiftList.App.ViewModels;
using SwiftList.Core;

namespace SwiftList.App.Views.InlineSearchWindow.Helpers
{
    public class InlineSearchWindowInputHandler
    {
        private readonly SwiftList.App.InlineSearchWindow _window;
        private readonly InlineSearchWindowLayoutManager _layoutManager;
        private bool _suppressExplorerSelectionSync;

        public InlineSearchWindowInputHandler(SwiftList.App.InlineSearchWindow window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _layoutManager = new InlineSearchWindowLayoutManager(window);
        }

        public void HandlePreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            // Escape key
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (_window.MenuPresenter.IsInActionsMode)
                {
                    _window.MenuPresenter.ExitActionsMode();
                }
                else if (_window.Manager.ExplorerTracker.IsActiveWindowDialog)
                {
                    _window.ResetInlineSearchAndFocusDialog();
                }
                else
                {
                    _window.HideWindow();
                }
                return;
            }

            // Enter key
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (_window.MenuPresenter.IsInActionsMode)
                {
                    _window.MenuPresenter.ExecuteSelectedAction();
                    return;
                }
                if (_window.LstResults.SelectedItem is AppSearchResult result)
                {
                    _window.ExecuteSearchResult(result);
                }
                else if (_window.LstResults.Items.Count > 0)
                {
                    _window.LstResults.SelectedIndex = 0;
                    if (_window.LstResults.SelectedItem is AppSearchResult firstResult)
                    {
                        _window.ExecuteSearchResult(firstResult);
                    }
                }
                return;
            }

            // Up arrow
            if (e.Key == Key.Up)
            {
                e.Handled = true;
                if (_window.MenuPresenter.IsInActionsMode)
                {
                    _window.MenuPresenter.NavigateActionsList(-1);
                    return;
                }
                MoveResultSelection(-1);
                return;
            }

            // Down arrow
            if (e.Key == Key.Down)
            {
                e.Handled = true;
                if (_window.MenuPresenter.IsInActionsMode)
                {
                    _window.MenuPresenter.NavigateActionsList(1);
                    return;
                }
                MoveResultSelection(1);
                return;
            }

            // Left arrow / Backspace
            if (e.Key == Key.Left || e.Key == Key.Back)
            {
                if (_window.MenuPresenter.IsInActionsMode)
                {
                    e.Handled = true;
                    _window.MenuPresenter.GoBackMenuOrExit();
                }
                return;
            }

            // Right arrow
            if (e.Key == Key.Right)
            {
                if (_window.MenuPresenter.IsInActionsMode)
                {
                    e.Handled = true;
                    _window.MenuPresenter.EnterSubMenu();
                }
                else if (_window.LstResults.SelectedItem is AppSearchResult result)
                {
                    e.Handled = true;
                    _window.MenuPresenter.EnterActionsMode(result);
                }
                return;
            }

            // Custom Modifier + 1..9
            var selectIndexMod = UserSettings.Load().SelectIndexModifier;
            if (Keyboard.Modifiers == GetWpfModifier(selectIndexMod))
            {
                int num = -1;
                if (e.Key >= Key.D1 && e.Key <= Key.D9)
                    num = (int)e.Key - (int)Key.D1 + 1;
                else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9)
                    num = (int)e.Key - (int)Key.NumPad1 + 1;

                if (num >= 1 && num <= 9)
                {
                    e.Handled = true;
                    LaunchByShortcutIndex(num);
                    return;
                }
            }
        }

        public void QueueResultsLayoutUpdate()
        {
            _layoutManager.QueueResultsLayoutUpdate();
        }

        public void UpdateActionsLayout()
        {
            _layoutManager.UpdateActionsLayout();
        }

        public void UpdateShortcutHints()
        {
            _layoutManager.UpdateShortcutHints();
        }

        public void LaunchByShortcutIndex(int num)
        {
            if (num < 1 || num > 9) return;

            var scrollViewer = _layoutManager.GetScrollViewer(_window.LstResults);
            int firstVisible = scrollViewer != null ? (int)Math.Round(scrollViewer.VerticalOffset) : 0;
            int shortcutIndex = 1;

            for (int i = firstVisible; i < _window.LstResults.Items.Count; i++)
            {
                var item = _window.LstResults.Items[i] as AppSearchResult;
                if (item != null && !item.IsEmptyResult && !item.IsSearchSectionHeader)
                {
                    if (shortcutIndex == num)
                    {
                        _window.ExecuteSearchResult(item);
                        return;
                    }

                    shortcutIndex++;
                }
            }
        }

        public void SyncExplorerSelection()
        {
            if (_suppressExplorerSelectionSync)
                return;

            if (_window.LstResults.SelectedItem is not AppSearchResult result) return;
            if (result.FullPath == "__SHOW_MORE__") return;

            var tracker = _window.Manager.ExplorerTracker;
            if (tracker.ActiveInlineAdapter != null && tracker.ActiveHwnd != IntPtr.Zero)
            {
                tracker.ActiveInlineAdapter.OnSelectionChanged(tracker.ActiveHwnd, result.FullPath);
                return;
            }

            if (!tracker.IsExplorerOrDesktopActive || tracker.IsDesktop || tracker.ActiveHwnd == IntPtr.Zero)
            {
                return;
            }

            if (!string.IsNullOrEmpty(result.ParentDir))
            {
                return;
            }

            FileExecutor.TrySelectItemInExistingExplorer(result.FullPath, tracker.ActiveHwnd);
        }

        public void SuppressExplorerSelectionSyncForResultRefresh()
        {
            _suppressExplorerSelectionSync = true;
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _suppressExplorerSelectionSync = false;

                // Auto-select the first valid result if nothing is selected
                if (_window.LstResults.SelectedIndex < 0 || _window.LstResults.SelectedItem == null)
                {
                    for (int i = 0; i < _window.LstResults.Items.Count; i++)
                    {
                        if (_window.LstResults.Items[i] is AppSearchResult item
                            && !item.IsEmptyResult && !item.IsSearchSectionHeader)
                        {
                            _window.LstResults.SelectedIndex = i;
                            _window.LstResults.ScrollIntoView(_window.LstResults.SelectedItem);
                            break;
                        }
                    }
                }

                SyncExplorerSelection();
            }), DispatcherPriority.ContextIdle);
        }

        private void MoveResultSelection(int direction)
        {
            int count = _window.LstResults.Items.Count;
            if (count == 0) return;

            int index = _window.LstResults.SelectedIndex;
            for (int i = 0; i < count; i++)
            {
                index += direction;
                if (index < 0 || index >= count)
                    break;

                if (_window.LstResults.Items[index] is AppSearchResult item && !item.IsEmptyResult && !item.IsSearchSectionHeader)
                {
                    _window.LstResults.SelectedIndex = index;
                    _window.LstResults.ScrollIntoView(_window.LstResults.SelectedItem);
                    break;
                }
            }
        }

        public static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            return InlineSearchWindowLayoutManager.FindVisualParent<T>(child);
        }

        private ModifierKeys GetWpfModifier(string modifierStr)
        {
            if (string.IsNullOrEmpty(modifierStr)) return ModifierKeys.Control;
            switch (modifierStr.Trim().ToUpperInvariant())
            {
                case "ALT":
                    return ModifierKeys.Alt;
                case "SHIFT":
                    return ModifierKeys.Shift;
                case "WIN":
                case "WINDOWS":
                    return ModifierKeys.Windows;
                case "NONE":
                    return ModifierKeys.None;
                default:
                    return ModifierKeys.Control;
            }
        }
    }
}
