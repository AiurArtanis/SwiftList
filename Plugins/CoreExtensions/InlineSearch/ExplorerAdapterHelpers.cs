using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.Plugins.CoreExtensions.InlineSearch;

internal static class ExplorerAdapterHelpers
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static string GetProcessName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != 0)
            {
                using var proc = Process.GetProcessById((int)pid);
                return proc.ProcessName;
            }
        }
        catch { }
        return "Unknown";
    }

    public static dynamic? FindExplorerWindow(IntPtr explorerHwnd)
    {
        var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
        if (shellWindowsType == null) return null;

        dynamic shellWindows = Activator.CreateInstance(shellWindowsType)!;
        int count = shellWindows.Count;

        for (var i = 0; i < count; i++)
        {
            try
            {
                dynamic? window = shellWindows.Item(i);
                if (window == null) continue;

                if ((IntPtr)window.HWND == explorerHwnd)
                {
                    return window;
                }
            }
            catch { }
        }
        return null;
    }

    public static async void SelectItemInExplorerLater(string path, IntPtr explorerHwnd)
    {
        await Task.Delay(250);
        try
        {
            dynamic? window = FindExplorerWindow(explorerHwnd);
            if (window == null) return;

            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return;

            dynamic folder = window.Document.Folder;
            dynamic? item = folder.ParseName(name);
            if (item == null) return;

            const int svsiSelect = 0x1;
            const int svsiDeselectOthers = 0x4;
            const int svsiEnsureVisible = 0x8;
            window.Document.SelectItem(item, svsiSelect | svsiDeselectOthers | svsiEnsureVisible);
        }
        catch { }
    }
}
