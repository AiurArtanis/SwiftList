using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SwiftList.Core;
using SwiftList.App.Helpers;

namespace SwiftList.App.Views.InlineSearchWindow.Helpers;

public class InlineSearchWindowInputHandler
{
    private readonly SwiftList.App.InlineSearchWindow _window;
    private readonly InlineSearchWindowLayoutManager _layoutManager;
    private bool _suppressExplorerSelectionSync;
    private bool _userNavigatedSinceLastQuery;

    public void ResetUserNavigation() => _userNavigatedSinceLastQuery = false;

    public InlineSearchWindowInputHandler(SwiftList.App.InlineSearchWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _layoutManager = new InlineSearchWindowLayoutManager(window);
    }

    public void HandlePreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (SearchInputHelper.HandleCommonSearchKeys(e, _window, _window.MenuPresenter))
            return;

        // Every bare key check below (including Escape) requires no modifiers -- otherwise it would
        // shadow a user-configurable combo hotkey sharing the same base key (e.g. CompleteFromSelectionHotkey
        // defaults to Ctrl+Tab, StartupPanel's tab-cycle hotkeys default to Ctrl+Left/Right) before it
        // ever reaches that hotkey's own dispatch further down (or the calling window's).
        var noModifiers = Keyboard.Modifiers == ModifierKeys.None;

        if (e.Key == Key.Tab && noModifiers)
        {
            e.Handled = true;
            return;
        }

        // Escape key
        if (e.Key == Key.Escape && noModifiers)
        {
            e.Handled = true;
            if (_window.Manager.ExplorerTracker.IsActiveWindowDialog)
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
        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (actualKey == Key.Enter)
        {
            e.Handled = true;

            var result = _window.LstResults.SelectedItem as AppSearchResult;
            if (result == null && _window.LstResults.Items.Count > 0)
            {
                _window.LstResults.SelectedIndex = 0;
                result = _window.LstResults.SelectedItem as AppSearchResult;
            }

            // File/folder results are handled earlier by HotkeyActionTrigger (Ctrl+Enter locate,
            // Ctrl+Shift+Enter open-as-admin) and never reach here. What reaches here on those chords
            // is a result with no matching file action — notably an application — so honor
            // Ctrl+Shift+Enter as "launch as admin" so apps can still be elevated.
            if (result != null)
            {
                var asAdmin = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
                ExecuteResult(result, asAdmin: asAdmin);
            }
            return;
        }

        // Up arrow
        if (actualKey == Key.Up && noModifiers)
        {
            e.Handled = true;
            MoveResultSelection(-1);
            return;
        }

        // Down arrow
        if (actualKey == Key.Down && noModifiers)
        {
            e.Handled = true;
            MoveResultSelection(1);
            return;
        }

        // Right arrow
        if (e.Key == Key.Right && noModifiers)
        {
            if (_window.LstResults.SelectedItem is AppSearchResult result)
            {
                e.Handled = true;
                _window.MenuPresenter.EnterActionsMode(result);
            }
            return;
        }

        // Next/previous item + actions menu + jump-to-item shortcuts

        var settings = UserSettings.Load().Hotkeys;
        if (WpfUiHelper.MatchesHotkey(settings.NextItemHotkey, Keyboard.Modifiers, actualKey))
        {
            e.Handled = true;
            MoveResultSelection(1);
            return;
        }
        if (WpfUiHelper.MatchesHotkey(settings.PreviousItemHotkey, Keyboard.Modifiers, actualKey))
        {
            e.Handled = true;
            MoveResultSelection(-1);
            return;
        }
        if (WpfUiHelper.MatchesHotkey(settings.ActionsMenuHotkey, Keyboard.Modifiers, actualKey))
        {
            if (_window.LstResults.SelectedItem is AppSearchResult result && !result.IsEmptyResult && !result.IsSearchSectionHeader)
            {
                e.Handled = true;
                _window.MenuPresenter.EnterActionsMode(result);
                return;
            }
        }

        if (!string.IsNullOrEmpty(settings.SelectJumpModifier) && Keyboard.Modifiers == WpfUiHelper.GetWpfModifier(settings.SelectJumpModifier))
        {
            var num = -1;
            if (actualKey >= Key.D1 && actualKey <= Key.D9)
                num = (int)actualKey - (int)Key.D1 + 1;
            else if (actualKey >= Key.NumPad1 && actualKey <= Key.NumPad9)
                num = (int)actualKey - (int)Key.NumPad1 + 1;
            if (num >= 1 && num <= 9)
            {
                e.Handled = true;
                LaunchByShortcutIndex(num);
                return;
            }
        }
    }

    public void QueueResultsLayoutUpdate() => _layoutManager.QueueResultsLayoutUpdate();

    public void UpdateActionsLayout() => _layoutManager.UpdateActionsLayout();

    public void UpdateShortcutHints() => _layoutManager.UpdateShortcutHints();

    public void UpdatePathPreviewVisibility() => _layoutManager.UpdatePathPreviewVisibility();

    public void SetHoveredResult(AppSearchResult? result) => _layoutManager.SetHoveredResult(result);

    public void LaunchByShortcutIndex(int num)
    {
        if (num < 1 || num > 9) return;
        var scrollViewer = _layoutManager.GetScrollViewer(_window.LstResults);
        var firstVisible = scrollViewer != null ? (int)Math.Round(scrollViewer.VerticalOffset) : 0;
        var shortcutIndex = 1;
        for (var i = firstVisible; i < _window.LstResults.Items.Count; i++)
        {
            if (_window.LstResults.Items[i] is AppSearchResult item && !item.IsEmptyResult && !item.IsSearchSectionHeader)
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
        }
    }

    public void SuppressExplorerSelectionSyncForResultRefresh()
    {
        _suppressExplorerSelectionSync = true;

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            _suppressExplorerSelectionSync = false;

            // Auto-select the first valid result if nothing is selected or if user has not navigated yet
            if (!_userNavigatedSinceLastQuery || _window.LstResults.SelectedIndex < 0 || _window.LstResults.SelectedItem == null)
            {
                for (var i = 0; i < _window.LstResults.Items.Count; i++)
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
        // Wraps like the actions list's NavigateActionsList (ShellMenuPresenter.cs) -- past the last
        // item goes back to the first, and vice versa.
        _userNavigatedSinceLastQuery = true;
        var count = _window.LstResults.Items.Count;
        if (count == 0) return;
        var index = _window.LstResults.SelectedIndex;
        var originalIndex = index;

        do
        {
            index = (index + direction + count) % count;
            if (index == originalIndex) break;
            if (_window.LstResults.Items[index] is AppSearchResult item && !item.IsEmptyResult && !item.IsSearchSectionHeader)
            {
                _window.LstResults.SelectedIndex = index;
                _window.LstResults.ScrollIntoView(_window.LstResults.SelectedItem);
                break;
            }
        } while (true);
    }

    public static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject => InlineSearchWindowLayoutManager.FindVisualParent<T>(child);

    private void ExecuteResult(AppSearchResult result, bool asAdmin)
    {
        if (asAdmin)
            _window.ExecuteSearchResultAsAdmin(result);
        else
            _window.ExecuteSearchResult(result);
    }
}
