using System.IO;
using System.Diagnostics;
using SwiftList.Core;

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
    public static void NavigateOrOpen(string path, IntPtr dialogHwndAtTrigger = default)
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
            var dialogTarget = File.Exists(path) ? Path.GetDirectoryName(path) : path;
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
        if (tracker.ActiveInlineAdapter != null && tracker.ActiveHwnd != IntPtr.Zero
            && tracker.ActiveInlineAdapter.ExecuteItem(tracker.ActiveHwnd, path, string.Empty))
        {
            return;
        }

        try
        {
            var workingDir = Path.GetDirectoryName(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                WorkingDirectory = string.IsNullOrEmpty(workingDir) ? "" : workingDir
            });
        }
        catch { }
    }
}
