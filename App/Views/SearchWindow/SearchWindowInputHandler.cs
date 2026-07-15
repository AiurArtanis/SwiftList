using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SwiftList.App.Services;
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
        // While the action flyout is open it owns navigation; still let action hotkeys fire on the item
        // (Ctrl+C etc.), then stand down so arrows/enter drive the flyout, not the result list behind it.
        if (ActionFlyout.IsOpen)
        {
            if (SearchInputHelper.TryActionHotkey(e, _window, _window.MenuPresenter))
                ActionFlyout.Close();
            return;
        }

        if (SearchInputHelper.HandleCommonSearchKeys(e, _window, _window.MenuPresenter))
            return;

        // Normal mode keys
        if (Keyboard.FocusedElement == _window.TxtSearchBoxControl &&
            (e.Key == Key.Down || e.Key == Key.Up || e.Key == Key.Enter))
        {
            HandleTxtSearchBoxKeyDown(e);
            return;
        }

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
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

        // The action menu opens on right-click only. Keyboard access to actions is via the registered
        // action hotkeys (Ctrl+C, Ctrl+Enter, ...), handled directly on the item by HandleCommonSearchKeys
        // above — no menu needed.
    }

    public void HandleTxtSearchBoxKeyDown(KeyEventArgs e)
    {
        var actualKey = WpfUiHelper.GetActualKey(e);
        // Down/Up require no modifiers so a future combo hotkey sharing either base key (this window
        // doesn't wire any today, but QuickSearchWindow's equivalent does) wouldn't get shadowed here.
        if (actualKey == Key.Down && Keyboard.Modifiers == ModifierKeys.None)
        {
            MoveSelection(1);
            e.Handled = true;
        }

        else if (actualKey == Key.Up && Keyboard.Modifiers == ModifierKeys.None)
        {
            MoveSelection(-1);
            e.Handled = true;
        }

        else if (actualKey == Key.Enter)
        {
            // File/folder results are handled earlier by HotkeyActionTrigger (Ctrl+Enter locate,
            // Ctrl+Shift+Enter open-as-admin) and never reach here. What reaches here on those chords
            // is a result with no matching file action — notably an application — so honor
            // Ctrl+Shift+Enter as "launch as admin" so apps can still be elevated.
            if (_window.LstGridResultsControl.SelectedItem is AppSearchResult)
            {
                var asAdmin = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
                OpenSelectedResult(asAdmin: asAdmin);
            }
            e.Handled = true;
        }
    }

    private IReadOnlyList<AppSearchResult> GetSelectedResults()
    {
        var list = new List<AppSearchResult>();
        foreach (var obj in _window.LstGridResultsControl.SelectedItems)
            if (obj is AppSearchResult r) list.Add(r);
        return list;
    }

    // Mouse events can originate from a non-Visual ContentElement (e.g. a highlight Run inside a result's
    // name TextBlock); VisualTreeHelper.GetParent throws on those, so step to the content parent instead.
    private static DependencyObject? VisualOrContentParent(DependencyObject dep)
        => dep is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(dep)
            : (dep as FrameworkContentElement)?.Parent;

    public void HandleLstGridResultsMouseDoubleClick(MouseButtonEventArgs e)
    {
        var depObj = e.OriginalSource as DependencyObject;
        while (depObj != null && !(depObj is ListViewItem))
        {
            if (depObj is GridViewColumnHeader)
            {
                return; // Ignore double clicks on column headers!
            }

            depObj = VisualOrContentParent(depObj);
        }

        if (depObj is ListViewItem item && item.Content is AppSearchResult result)
        {
            e.Handled = true;
            var selectModifier = ModifierKeys.Control;
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
            var selectModifier = ModifierKeys.Control;
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
        // Open every selected result (the grid list allows multi-selection).
        var opened = 0;
        foreach (var obj in _window.LstGridResultsControl.SelectedItems)
        {
            if (obj is AppSearchResult r && !r.IsSearchSectionHeader && !r.IsEmptyResult && !string.IsNullOrEmpty(r.FullPath))
            {
                if (asAdmin)
                    FileExecutor.OpenFileOrFolderAsAdmin(r.FullPath);
                else
                    FileExecutor.OpenFileOrFolder(r.FullPath);
                opened++;
            }
        }

        if (opened == 0 && _window.LstGridResultsControl.SelectedItem is AppSearchResult selected && !string.IsNullOrEmpty(selected.FullPath))
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
            element = VisualOrContentParent(element);
        }

        if (element is ListViewItem listViewItem && listViewItem.Content is AppSearchResult result)
        {
            e.Handled = true;
            // Preserve an existing multi-selection when right-clicking one of its members;
            // otherwise select just the right-clicked item.
            if (!_window.LstGridResultsControl.SelectedItems.Contains(result))
                _window.LstGridResultsControl.SelectedItem = result;

            // Show the action flyout at the cursor, anchored to the right-clicked row.
            ShowActionFlyout(PlacementMode.MousePoint, listViewItem);
        }
    }

    // Opens the action flyout for the current selection. Gated by the same CanShowActionsMenu check the
    // old in-window actions panel used, so apps / plugin results / empty rows still suppress it.
    private void ShowActionFlyout(PlacementMode placement, UIElement? anchor = null)
    {
        var selection = GetSelectedResults();
        if (_window.MenuPresenter?.CanShowActionsMenu(selection) != true)
            return;

        if (anchor == null)
        {
            // Keyboard-triggered: anchor to the selected row's container. Realize it first (scroll into
            // view) so the popup lands on-screen; if it still isn't realized, fall back to the search box
            // so the flyout is always visible instead of anchoring off the bottom of the list.
            var lst = _window.LstGridResultsControl;
            var selected = lst.SelectedItem;
            if (selected != null)
            {
                lst.ScrollIntoView(selected);
                lst.UpdateLayout();
            }
            anchor = lst.ItemContainerGenerator.ContainerFromItem(selected) as UIElement;
            if (anchor == null)
            {
                anchor = _window.TxtSearchBoxControl;
                placement = PlacementMode.Bottom;
            }
        }

        ActionFlyout.Show(selection, _window, _window, anchor, placement);
    }
}
