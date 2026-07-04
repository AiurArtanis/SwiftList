using System.IO;
using System.Diagnostics;
using SwiftList.Core;

namespace SwiftList.App.Services;

public static class QuickNavigationNavigator
{
    public static void NavigateOrOpen(string path)
    {
        // Web-address favorites: straight to the default browser (no working dir / explorer navigation).
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
        }
        else if (Directory.Exists(path) && tracker.IsExplorerOrDesktopActive && !tracker.IsDesktop && tracker.ActiveHwnd != IntPtr.Zero)
        {
            FileExecutor.TryLocateInExistingExplorer(path, tracker.ActiveHwnd);
        }
        else
        {
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
}
