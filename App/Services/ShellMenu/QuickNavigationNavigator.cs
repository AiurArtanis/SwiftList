using System.IO;
using System.Diagnostics;
using SwiftList.Core;
using SwiftList.Core.Hook;

namespace SwiftList.App.Services;

public static class QuickNavigationNavigator
{
    // dialogHwndAtTrigger: the dialog hwnd to navigate, captured by the caller BEFORE showing any UI of its
    // own (e.g. QuickNavigationMenu.Show, right when it reads ExplorerTracker) -- IntPtr.Zero if no dialog
    // was active at that moment. Re-reading ExplorerTracker.ActiveHwnd/IsActiveWindowDialog live at click
    // time is not safe here: a Quick Navigation popup can sit open for a while, and closing its own helper
    // window on click hands OS foreground back to whatever window "owned" it before the popup stole focus --
    // often the very dialog the user clicked away from -- which flips the tracker back to "dialog active"
    // just before this runs, silently redirecting the click into a dialog the user never touched.
    // isDir: already known by the caller (e.g. QuickNavigationMenu's item.HasSubMenu) -- see
    // InlineAdapterIpcCoordinator.ExecuteItem for why the Hook process must never be asked to re-derive
    // this itself via Directory.Exists/File.Exists.
    public static void NavigateOrOpen(string path, bool isDir, IntPtr dialogHwndAtTrigger = default)
    {
        // Web-address favorites: straight to the default browser. No host file-manager adapter understands
        // a URL as a filesystem path, so this must short-circuit before any adapter delegation below.
        if (Helpers.FavoriteUrlHelper.IsWebUrl(path))
        {
            try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); } catch { }
            return;
        }

        if (dialogHwndAtTrigger != IntPtr.Zero)
        {
            // A dialog's filename box submits (fires Open/Save) if given a complete, existing file path --
            // never let picking a file from a Quick Navigation menu auto-confirm the dialog on the user's
            // behalf; only ever navigate it to a directory, same as clicking a folder would.
            var dialogTarget = isDir ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dialogTarget)) return;

            App.HookClient?.SendMessage(new IpcMessage
            {
                Id = IpcMessageId.NavigateDialog,
                Hwnd = dialogHwndAtTrigger.ToInt64(),
                StringVal1 = dialogTarget
            });
            return;
        }

        // Delegate to whichever file-manager adapter matched the active host (Explorer, Directory Opus,
        // Total Commander, ...) so a folder navigates that window and a file opens/selects there -- the
        // same adapter inline search already uses to execute a result.
        var tracker = InlineSearchManager.Instance.ExplorerTracker;
        if (tracker.ActiveInlineAdapter != null && tracker.ActiveHwnd != IntPtr.Zero && App.HookClient?.IsConnected == true)
        {
            if (InlineAdapterIpcCoordinator.ExecuteItem(tracker.ActiveHwnd, path, isDir, string.Empty, App.HookClient.SendMessage, out var lateResult))
                return;

            // Timed out without a confirmed result -- some adapters make blocking calls with no timeout of
            // their own (e.g. Total Commander's SendMessage), so the Hook-side call can still legitimately
            // be in flight rather than genuinely dead. Falling back to Process.Start right here used to be
            // able to race that: the file gets launched/opened by the fallback AND separately
            // navigated-to-and-selected by the adapter call finishing a moment later. See
            // InlineAdapterIpcCoordinator.RunAfterLateResultAsync -- also used by inline search's own
            // Enter-to-execute for the identical race -- for why waiting on the same in-flight call a bit
            // longer, off the UI thread, closes that window without blocking the caller.
            _ = InlineAdapterIpcCoordinator.RunAfterLateResultAsync(lateResult, onSuccess: () => { }, onFallback: () => OpenDirectly(path, isDir));
            return;
        }

        OpenDirectly(path, isDir);
    }

    // This is a NAVIGATION menu -- picking a file here should land on it (selected, in its folder), never
    // launch it. A directory still opens/navigates into it via ShellExecute (that already just changes
    // Explorer's own location, no different from "navigating" there); a file instead goes through
    // FileExecutor.LocateInExplorer, the same "select this item" routine LocateInExplorerExternal uses,
    // which opens/reuses an Explorer window at the file's parent folder with the file selected rather than
    // running its associated program.
    private static void OpenDirectly(string path, bool isDir)
    {
        if (!isDir)
        {
            FileExecutor.LocateInExplorer(path);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
