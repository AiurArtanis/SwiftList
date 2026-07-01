using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftList.App.Services;
using SwiftList.Core;
using SwiftList.App.Helpers;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListViewItem = System.Windows.Controls.ListViewItem;
namespace SwiftList.App.Views.SearchWindow;

public class SearchWindowInputHandler
{
    private readonly SwiftList.App.SearchWindow _window;

    public SearchWindowInputHandler(SwiftList.App.SearchWindow window) => _window = window;

    public void HandleWindowPreviewKeyDown(KeyEventArgs e)
    {
        if (SearchInputHelper.HandleCommonSearchKeys(e, _window, _window.MenuPresenter))
            return;

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
                _window.MenuPresenter?.EnterActionsMode(result);
                e.Handled = true;
                return;
            }
        }

        var selectIndexMod = UserSettings.Load().SelectIndexModifier;
        if (Keyboard.Modifiers == WpfUiHelper.GetWpfModifier(selectIndexMod))
        {
            var actualKey = WpfUiHelper.GetActualKey(e);
            if (actualKey == Key.O)
            {
                if (_window.LstGridResultsControl.SelectedItem is AppSearchResult result && !result.IsEmptyResult && !result.IsSearchSectionHeader)
                {
                    _window.MenuPresenter?.EnterActionsMode(result);
                    e.Handled = true;
                    return;
                }
            }
        }
    }

    public void HandleTxtSearchBoxKeyDown(KeyEventArgs e)
    {
        var actualKey = WpfUiHelper.GetActualKey(e);
        if (actualKey == Key.Down)
        {
            MoveSelection(1);
            e.Handled = true;
        }

        else if (actualKey == Key.Up)
        {
            MoveSelection(-1);
            e.Handled = true;
        }

        else if (actualKey == Key.Enter)
        {
            var selectModifier = WpfUiHelper.GetWpfModifier(UserSettings.Load().SelectIndexModifier);
            var currentModifiers = Keyboard.Modifiers;
            if (_window.LstGridResultsControl.SelectedItem is AppSearchResult selected)
            {
                var isFileOrFolder = !selected.IsSearchSectionHeader && !selected.IsEmptyResult &&
                    (selected.ResultKind == "File" || selected.ResultKind == "Folder" || System.IO.File.Exists(selected.FullPath) || System.IO.Directory.Exists(selected.FullPath));

                if (currentModifiers == selectModifier && isFileOrFolder)
                {
                    FileExecutor.LocateInExplorer(selected.FullPath);
                    _window.Close();
                }
                else if (currentModifiers == (selectModifier | ModifierKeys.Shift))
                {
                    OpenSelectedResult(asAdmin: true);
                }
                else
                {
                    OpenSelectedResult(asAdmin: false);
                }
            }
            e.Handled = true;
        }
    }

    public void HandleLstGridResultsPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
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

        if (depObj is ListViewItem item && item.Content is AppSearchResult result)
        {
            e.Handled = true;
            var selectModifier = WpfUiHelper.GetWpfModifier(UserSettings.Load().SelectIndexModifier);
            var currentModifiers = Keyboard.Modifiers;
            var isFileOrFolder = !result.IsSearchSectionHeader && !result.IsEmptyResult &&
                (result.ResultKind == "File" || result.ResultKind == "Folder" || System.IO.File.Exists(result.FullPath) || System.IO.Directory.Exists(result.FullPath));

            if (currentModifiers == selectModifier && isFileOrFolder)
            {
                FileExecutor.LocateInExplorer(result.FullPath);
                _window.Close();
            }
            else if (currentModifiers == (selectModifier | ModifierKeys.Shift))
            {
                FileExecutor.OpenFileOrFolderAsAdmin(result.FullPath);
            }
            else
            {
                FileExecutor.OpenFileOrFolder(result.FullPath);
            }
        }
    }

    public void HandleLstGridResultsKeyDown(KeyEventArgs e)
    {
        var actualKey2 = WpfUiHelper.GetActualKey(e);
        if (actualKey2 == Key.Enter)
        {
            var selectModifier = WpfUiHelper.GetWpfModifier(UserSettings.Load().SelectIndexModifier);
            var currentModifiers = Keyboard.Modifiers;
            if (_window.LstGridResultsControl.SelectedItem is AppSearchResult selected)
            {
                var isFileOrFolder = !selected.IsSearchSectionHeader && !selected.IsEmptyResult &&
                    (selected.ResultKind == "File" || selected.ResultKind == "Folder" || System.IO.File.Exists(selected.FullPath) || System.IO.Directory.Exists(selected.FullPath));

                if (currentModifiers == selectModifier && isFileOrFolder)
                {
                    FileExecutor.LocateInExplorer(selected.FullPath);
                    _window.Close();
                }
                else if (currentModifiers == (selectModifier | ModifierKeys.Shift))
                {
                    OpenSelectedResult(asAdmin: true);
                }
                else
                {
                    OpenSelectedResult(asAdmin: false);
                }
            }
            e.Handled = true;
        }
    }

    public void OpenSelectedResult(bool asAdmin = false)
    {
        if (_window.LstGridResultsControl.SelectedItem is AppSearchResult selected)
        {
            if (asAdmin)
                FileExecutor.OpenFileOrFolderAsAdmin(selected.FullPath);
            else
                FileExecutor.OpenFileOrFolder(selected.FullPath);
        }
    }

    private void MoveSelection(int delta)
    {
        var count = _window.LstGridResultsControl.Items.Count;
        if (count == 0)
        {
            _window.LstGridResultsControl.SelectedIndex = -1;
            return;
        }

        var current = _window.LstGridResultsControl.SelectedIndex;
        var next = current < 0 ? 0 : Math.Clamp(current + delta, 0, count - 1);
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

    private bool IsSearchCaretAtEnd() => _window.TxtSearchBoxControl.IsKeyboardFocusWithin

               && _window.TxtSearchBoxControl.SelectionLength == 0

               && _window.TxtSearchBoxControl.CaretIndex >= _window.TxtSearchBoxControl.Text.Length;
}
