using System.IO;
using System.Diagnostics;
using SwiftList.Core;

namespace SwiftList.App.Services;

public static class QuickNavigationNavigator
{
    public static void NavigateOrOpen(string path)
    {
        // Web-address favorites: straight to the default browser. No host file-manager adapter understands
        // a URL as a filesystem path, so this must short-circuit before any adapter delegation below.
        if (Helpers.FavoriteUrlHelper.IsWebUrl(path))
        {
            try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); } catch { }
            return;
        }

        var tracker = InlineSearchManager.Instance.ExplorerTracker;
        if (tracker.IsExplorerOrDesktopActive && tracker.IsActiveWindowDialog && tracker.ActiveHwnd != IntPtr.Zero)
        {
            App.HookClient?.SendMessage(new IpcMessage
            {
                Id = IpcMessageId.NavigateDialog,
                Hwnd = tracker.ActiveHwnd.ToInt64(),
                StringVal1 = path
            });
            return;
        }

        // Delegate to whichever file-manager adapter matched the active host (Explorer, Directory Opus,
        // Total Commander, ...) so a folder navigates that window and a file opens/selects there -- the
        // same adapter inline search already uses to execute a result.
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
