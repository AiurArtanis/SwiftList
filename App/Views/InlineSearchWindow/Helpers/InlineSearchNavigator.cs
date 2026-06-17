using System.IO;
using System.Runtime.InteropServices;
using SwiftList.App.Services;
using SwiftList.Core;
namespace SwiftList.App.Views.InlineSearchWindow.Helpers;

public static class InlineSearchNavigator
{
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    public static void LocateInExplorerExternal(this SwiftList.App.InlineSearchWindow window, string path)
    {
        var tracker = window.Manager.ExplorerTracker;
        if (tracker.IsExplorerOrDesktopActive && !tracker.IsDesktop && tracker.ActiveHwnd != IntPtr.Zero)
        {
            if (FileExecutor.TryLocateInExistingExplorer(path, tracker.ActiveHwnd))
            {
                return;
            }
        }

        FileExecutor.LocateInExplorer(path);
    }

    public static void OpenFileOrFolderExternal(this SwiftList.App.InlineSearchWindow window, string path) => window.OpenPathFromInline(path, asAdmin: false);

    public static void OpenFileOrFolderAsAdminExternal(this SwiftList.App.InlineSearchWindow window, string path) => window.OpenPathFromInline(path, asAdmin: true);

    public static void ExecuteSearchResult(this SwiftList.App.InlineSearchWindow window, AppSearchResult result, bool asAdmin = false)
    {
        if (result.IsSearchSectionHeader)
            return;
        if (result.IsPluginSearchAction)
        {
            if (PluginManager.Instance.TryExecuteSearchAction(result, window))
            {
                window.HideWindow();
            }

            return;
        }

        if (PluginManager.Instance.TryExecuteSearchAction(result, window))
        {
            window.HideWindow();
            return;
        }

        window.OpenPathFromInline(result.FullPath, asAdmin);
    }

    private static void OpenPathFromInline(this SwiftList.App.InlineSearchWindow window, string path, bool asAdmin)
    {
        var tracker = window.Manager.ExplorerTracker;
        if (!asAdmin && path != "__SHOW_MORE__" && tracker.ActiveInlineAdapter != null && tracker.ActiveHwnd != IntPtr.Zero)
        {
            window.Manager.IsExecuting = true;
            if (tracker.ActiveInlineAdapter.ExecuteItem(tracker.ActiveHwnd, path, window.SearchText))
            {
                window.HideWindow();
                return;
            }

            window.Manager.IsExecuting = false;
        }

        if (path != "__SHOW_MORE__" && tracker.IsExplorerOrDesktopActive && tracker.IsActiveWindowDialog && tracker.ActiveHwnd != IntPtr.Zero)
        {
            App.HookClient?.SendMessage(new IpcMessage
            {
                Id = IpcMessageId.NavigateDialog,
                Hwnd = tracker.ActiveHwnd.ToInt64(),
                StringVal1 = path

            });

            window.UpdateSearchDisplay(string.Empty);
            window.HideWindow();
            return;
        }

        if (Directory.Exists(path)
            && tracker.IsExplorerOrDesktopActive

            && !tracker.IsDesktop

            && tracker.ActiveHwnd != IntPtr.Zero

            && FileExecutor.TryLocateInExistingExplorer(path, tracker.ActiveHwnd))
        {
            window.HideWindow();
            return;
        }

        var searchText = window.SearchText;
        if (path != "__SHOW_MORE__")
        {
            window.HideWindow();
            if (asAdmin)
                FileExecutor.OpenFileOrFolderAsAdmin(path, searchText);
            else
                FileExecutor.OpenFileOrFolder(path, searchText);
            return;
        }

        FileExecutor.OpenFileOrFolder(path, searchText, () => window.HideWindow());
    }

    public static void ResetInlineSearchAndFocusDialog(this SwiftList.App.InlineSearchWindow window)
    {
        // 1. Clear our own search query

        window.UpdateSearchDisplay(string.Empty);

        // 2. Grant the elevated hook service permission to call SetForegroundWindow,
        //    bypassing the system's foreground-lock without needing to hide this window.

        if (App.HookClient != null && App.HookClient.ServiceProcessId != 0)
        {
            AllowSetForegroundWindow(App.HookClient.ServiceProcessId);
        }

        // 3. Ask the elevated service to restore focus to the dialog's edit box

        var tracker = window.Manager.ExplorerTracker;
        if (tracker.ActiveHwnd != IntPtr.Zero && App.HookClient != null)
        {
            App.HookClient.SendMessage(new IpcMessage
            {
                Id = IpcMessageId.RestoreDialogFocus,
                Hwnd = tracker.ActiveHwnd.ToInt64()

            });
        }
    }
}
