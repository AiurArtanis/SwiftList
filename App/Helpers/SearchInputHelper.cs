using System.Windows;
using System.Windows.Input;
using SwiftList.Core;
using SwiftList.App.Services;

namespace SwiftList.App.Helpers;

public static class SearchInputHelper
{
    public static bool IsQuickLookKey(System.Windows.Input.KeyEventArgs e)
    {
        var selectIndexMod = UserSettings.Load().SelectIndexModifier;
        var quickLookModifier = selectIndexMod.Trim().ToUpperInvariant() switch
        {
            "ALT" => ModifierKeys.Alt,
            "SHIFT" => ModifierKeys.Shift,
            "WIN" or "WINDOWS" => ModifierKeys.Windows,
            "NONE" => ModifierKeys.None,
            _ => ModifierKeys.Control,
        };
        var checkKey = e.Key == Key.System ? e.SystemKey : e.Key;
        return (checkKey == Key.P && Keyboard.Modifiers == quickLookModifier) ||
               (checkKey == Key.Space && Keyboard.Modifiers == quickLookModifier);
    }

    public static bool HandleActionsModeKeys(System.Windows.Input.KeyEventArgs e, ISearchWindow? window, ShellMenuPresenter? menuPresenter)
    {
        if (menuPresenter == null || !menuPresenter.IsInActionsMode)
            return false;

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

        if (e.Key == Key.Right || (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None))
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

    public static bool HandleCommonSearchKeys(System.Windows.Input.KeyEventArgs e, ISearchWindow window, ShellMenuPresenter? menuPresenter)
    {
        // 1. Actions Mode keys
        if (HandleActionsModeKeys(e, window, menuPresenter))
            return true;

        // 2. Clipboard Copy (Ctrl+C)
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (window.LstResults.SelectedItem is AppSearchResult result && !result.IsSearchSectionHeader && !result.IsEmptyResult)
            {
                if (result.ResultKind == "File" || result.ResultKind == "Folder" || System.IO.File.Exists(result.FullPath) || System.IO.Directory.Exists(result.FullPath))
                {
                    try
                    {
                        var fileList = new System.Collections.Specialized.StringCollection { result.FullPath };
                        System.Windows.Clipboard.SetFileDropList(fileList);
                        window.HideWindow();
                        e.Handled = true;
                        return true;
                    }
                    catch { }
                }
            }
        }

        // 3. QuickLook
        if (window.GetType().Name != "InlineSearchWindow" && IsQuickLookKey(e))
        {
            if (window.LstResults.SelectedItem is AppSearchResult result && !result.IsSearchSectionHeader && !result.IsEmptyResult && !result.IsApplication && result.FullPath != "__SHOW_MORE__")
            {
                QuickLookManager.Instance.Toggle((Window)window, result.FullPath);
                e.Handled = true;
                return true;
            }
        }

        return false;
    }
}
