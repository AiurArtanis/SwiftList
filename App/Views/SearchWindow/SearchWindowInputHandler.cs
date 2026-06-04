using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using SwiftList.App.Services;
using SwiftList.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListViewItem = System.Windows.Controls.ListViewItem;

namespace SwiftList.App.Views.SearchWindow
{
    public class SearchWindowInputHandler
    {
        private readonly SwiftList.App.SearchWindow _window;

        public SearchWindowInputHandler(SwiftList.App.SearchWindow window)
        {
            _window = window;
        }

        public void HandleWindowPreviewKeyDown(KeyEventArgs e)
        {
            var menuPresenter = _window.MenuPresenter;

            // Route actions mode keys if active
            if (menuPresenter != null && menuPresenter.IsInActionsMode)
            {
                if (e.Key == Key.Escape)
                {
                    menuPresenter.ExitActionsMode();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Left || e.Key == Key.Back)
                {
                    menuPresenter.GoBackMenuOrExit();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Right)
                {
                    menuPresenter.EnterSubMenu();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Down)
                {
                    menuPresenter.NavigateActionsList(1);
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Up)
                {
                    menuPresenter.NavigateActionsList(-1);
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Enter)
                {
                    menuPresenter.ExecuteSelectedAction();
                    e.Handled = true;
                    return;
                }

                if (e.Key != Key.System && e.Key != Key.LeftAlt && e.Key != Key.RightAlt)
                {
                    e.Handled = true;
                    return;
                }
            }

            // Normal mode keys
            if (Keyboard.FocusedElement == _window.TxtSearchBoxControl &&
                (e.Key == Key.Down || e.Key == Key.Up || e.Key == Key.Enter))
            {
                HandleTxtSearchBoxKeyDown(e);
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (string.IsNullOrEmpty(_window.TxtSearchBoxControl.Text))
                {
                    _window.Close();
                }
                else
                {
                    _window.TxtSearchBoxControl.Text = string.Empty;
                    _window.TxtSearchBoxControl.Focus();
                }
                e.Handled = true;
                return;
            }

            // Right arrow key enters Actions Mode if caret is at the end
            if (e.Key == Key.Right && IsSearchCaretAtEnd())
            {
                if (_window.LstGridResultsControl.SelectedItem is AppSearchResult result)
                {
                    menuPresenter?.EnterActionsMode(result);
                    e.Handled = true;
                    return;
                }
            }
        }

        public void HandleTxtSearchBoxKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                MoveSelection(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                MoveSelection(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                OpenSelectedResult();
                e.Handled = true;
            }
        }

        public void HandleLstGridResultsMouseDoubleClick(MouseButtonEventArgs e)
        {
            var depObj = e.OriginalSource as DependencyObject;
            while (depObj != null && !(depObj is ListViewItem))
            {
                if (depObj is GridViewColumnHeader)
                {
                    return; // Ignore double clicks on column headers!
                }
                depObj = System.Windows.Media.VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is ListViewItem)
            {
                OpenSelectedResult();
            }
        }

        public void HandleLstGridResultsKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OpenSelectedResult();
                e.Handled = true;
            }
        }

        public void OpenSelectedResult()
        {
            if (_window.LstGridResultsControl.SelectedItem is AppSearchResult selected)
            {
                FileExecutor.OpenFileOrFolder(selected.FullPath);
            }
        }

        private void MoveSelection(int delta)
        {
            int count = _window.LstGridResultsControl.Items.Count;
            if (count == 0)
            {
                _window.LstGridResultsControl.SelectedIndex = -1;
                return;
            }

            int current = _window.LstGridResultsControl.SelectedIndex;
            int next = current < 0 ? 0 : Math.Clamp(current + delta, 0, count - 1);
            _window.LstGridResultsControl.SelectedIndex = next;
            _window.LstGridResultsControl.ScrollIntoView(_window.LstGridResultsControl.SelectedItem);
        }

        public void HandleLstGridResultsPreviewMouseRightButtonUp(MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as DependencyObject;
            while (element != null && element is not ListViewItem)
            {
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }

            if (element is ListViewItem listViewItem && listViewItem.Content is AppSearchResult result)
            {
                e.Handled = true;
                _window.LstGridResultsControl.SelectedItem = result;
                
                // Trigger the shared premium actions context menu panel overlay
                _window.MenuPresenter.EnterActionsMode(result);
            }
        }

        private bool IsSearchCaretAtEnd()
        {
            return _window.TxtSearchBoxControl.IsKeyboardFocusWithin
                   && _window.TxtSearchBoxControl.SelectionLength == 0
                   && _window.TxtSearchBoxControl.CaretIndex >= _window.TxtSearchBoxControl.Text.Length;
        }
    }
}
