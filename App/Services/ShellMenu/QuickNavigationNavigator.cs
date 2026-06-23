using System.IO;
using System.Diagnostics;
using SwiftList.Core;

namespace SwiftList.App.Services;

public static class QuickNavigationNavigator
{
    public static void NavigateOrOpen(string path)
    {
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
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
