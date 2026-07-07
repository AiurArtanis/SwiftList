using System.Windows;
using System.Windows.Input;
using SwiftList.Core;
using SwiftList.App.Services;

namespace SwiftList.App.Helpers;

public static class SearchInputHelper
{
    public static bool IsQuickLookKey(System.Windows.Input.KeyEventArgs e)
    {
        var checkKey = e.Key == Key.System ? e.SystemKey : e.Key;
        return WpfUiHelper.MatchesHotkey(UserSettings.Load().Hotkeys.QuickLookHotkey, Keyboard.Modifiers, checkKey);
    }

    public static bool HandleActionsModeKeys(System.Windows.Input.KeyEventArgs e, ISearchWindow? window, ShellMenuPresenter? menuPresenter)
    {
        if (menuPresenter == null || !menuPresenter.IsInActionsMode)
            return false;

        // Read once up front: the custom next/previous-item hotkeys must win over the hardcoded bare-Tab
        // shortcut below whenever the user has bound one of them to Tab, otherwise a Tab-as-next-item
        // binding would silently be swallowed by "Tab enters submenu" and never reach the match further down.
        var actualKey = WpfUiHelper.GetActualKey(e);
        var settings = UserSettings.Load().Hotkeys;
        var isNextItemHotkey = WpfUiHelper.MatchesHotkey(settings.NextItemHotkey, Keyboard.Modifiers, actualKey);
        var isPreviousItemHotkey = WpfUiHelper.MatchesHotkey(settings.PreviousItemHotkey, Keyboard.Modifiers, actualKey);

        if (e.Key == Key.Escape)
        {
            if (window != null && !string.IsNullOrEmpty(window.SearchTextBox.Text))
            {
                window.SearchTextBox.Clear();
                e.Handled = true;
                return true;
            }
            menuPresenter.ExitActionsMode();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Left)
        {
            menuPresenter.GoBackMenuOrExit();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Right || (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None && !isNextItemHotkey && !isPreviousItemHotkey))
        {
            menuPresenter.EnterSubMenu();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Down)
        {
            menuPresenter.NavigateActionsList(1);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Up)
        {
            menuPresenter.NavigateActionsList(-1);
            e.Handled = true;
            return true;
        }

        // The results list also accepts the user's configurable next/previous-item hotkeys (not just the
        // literal arrow keys above); the actions list should match so a custom binding still works once
        // the menu is open instead of silently falling through to move the hidden results-list selection.
        if (isNextItemHotkey)
        {
            menuPresenter.NavigateActionsList(1);
            e.Handled = true;
            return true;
        }
        if (isPreviousItemHotkey)
        {
            menuPresenter.NavigateActionsList(-1);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Enter)
        {
            menuPresenter.ExecuteSelectedAction();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Back)
        {
            if (window != null && string.IsNullOrEmpty(window.SearchTextBox.Text))
            {
                menuPresenter.GoBackMenuOrExit();
                e.Handled = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Fires an action hotkey (e.g. Ctrl+C copy, Ctrl+Enter locate) on the selected result without
    /// opening any menu — the always-available behavior the quick window has. Only runs when a modifier
    /// is held and the actions menu is allowed for the selection, so plain typing pays no cost and
    /// suppressed rows (apps / plugin results / ...) suppress the hotkeys too.
    /// </summary>
    public static bool TryActionHotkey(System.Windows.Input.KeyEventArgs e, ISearchWindow window, ShellMenuPresenter? menuPresenter)
    {
        if (Keyboard.Modifiers != ModifierKeys.None
            && window.LstResults.SelectedItem is AppSearchResult selectedResult
            && menuPresenter != null
            && menuPresenter.CanShowActionsMenu(new[] { selectedResult }))
        {
            if (HotkeyActionTrigger.TryExecute(e, selectedResult, window))
            {
                e.Handled = true;
                return true;
            }
        }
        return false;
    }

    public static bool HandleCommonSearchKeys(System.Windows.Input.KeyEventArgs e, ISearchWindow window, ShellMenuPresenter? menuPresenter)
    {
        // 1. Actions Mode keys
        if (HandleActionsModeKeys(e, window, menuPresenter))
            return true;

        // 1b. Action hotkeys on the selected item (Ctrl+C copy, Ctrl+Enter locate, ...).
        if (TryActionHotkey(e, window, menuPresenter))
            return true;

        // 2. QuickLook
        if (window.GetType().Name != "InlineSearchWindow" && IsQuickLookKey(e))
        {
            if (window.LstResults.SelectedItem is AppSearchResult result && result.CanPreview)
            {
                QuickLookManager.Instance.Toggle((Window)window, result.FullPath);
                e.Handled = true;
                return true;
            }
        }

        return false;
    }
}
