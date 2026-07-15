using System.IO;
using System.Runtime.InteropServices;
using SwiftList.App.Services;
using SwiftList.Core;
using SwiftList.Core.Hook;
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

    public static void OpenFileOrFolderExternal(this SwiftList.App.InlineSearchWindow window, string path) => window.OpenPathFromInline(path, asAdmin: false, ResolveIsDir(path));

    public static void OpenFileOrFolderAsAdminExternal(this SwiftList.App.InlineSearchWindow window, string path) => window.OpenPathFromInline(path, asAdmin: true, ResolveIsDir(path));

    // null means "doesn't exist" -- distinct from false ("is a file"), so OpenPathFromInline can skip
    // ExecuteItem entirely for a path that no longer exists rather than asking an adapter to navigate a
    // third-party app to it. Directory.Exists(path) alone can't tell "is a file" apart from "doesn't exist
    // at all" (both false), which is exactly the ambiguity that needs resolving here.
    private static bool? ResolveIsDir(string path)
    {
        if (Directory.Exists(path)) return true;
        if (File.Exists(path)) return false;
        return null;
    }

    public static void ExecuteSearchResult(this SwiftList.App.InlineSearchWindow window, AppSearchResult result, bool asAdmin = false)
    {
        if (result.IsSearchSectionHeader)
            return;
        if (result.IsPluginSearchAction)
        {
            if (PluginManager.Instance.TryExecuteSearchAction(result, window, asAdmin))
            {
                window.HideWindow();
            }

            return;
        }

        if (PluginManager.Instance.TryExecuteSearchAction(result, window, asAdmin))
        {
            window.HideWindow();
            return;
        }

        // Trust result.IsDir for *which kind* it is (that's already known from the index), but still
        // confirm the path actually still exists right now -- a search result can go stale between when it
        // was indexed and when the user acts on it (e.g. the file was deleted in between).
        var exists = result.IsDir ? Directory.Exists(result.FullPath) : File.Exists(result.FullPath);
        window.OpenPathFromInline(result.FullPath, asAdmin, exists ? result.IsDir : (bool?)null);
    }

    // isDir: null means the path doesn't exist (skip ExecuteItem, go straight to the "not found" fallback
    // below); otherwise the caller's already-known answer for file vs directory -- see
    // InlineAdapterIpcCoordinator.ExecuteItem for why the Hook process must never be asked to re-derive
    // this itself via Directory.Exists/File.Exists.
    private static void OpenPathFromInline(this SwiftList.App.InlineSearchWindow window, string path, bool asAdmin, bool? isDir)
    {
        var tracker = window.Manager.ExplorerTracker;
        if (!asAdmin && isDir.HasValue && path != "__SHOW_MORE__" && tracker.ActiveInlineAdapter != null && tracker.ActiveHwnd != IntPtr.Zero && App.HookClient?.IsConnected == true)
        {
            window.Manager.IsExecuting = true;
            if (InlineAdapterIpcCoordinator.ExecuteItem(tracker.ActiveHwnd, path, isDir.Value, window.SearchText, App.HookClient.SendMessage))
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

        if (isDir == true
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
