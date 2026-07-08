using System.IO;
using System.Runtime.InteropServices;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;
using SwiftList.Plugins.TotalCommander.Win32;

namespace SwiftList.Plugins.TotalCommander;

/// <summary>
/// Inline-search integration for Total Commander (TTOTAL_CMD). Reading the current path and navigating the
/// active pane both go through TC's documented WM_COPYDATA remote-control interface (see Win32Helper), so no
/// scraping of TC's custom-drawn controls is involved.
/// </summary>
public class TotalCommanderInlineSearchAdapter : IInlineSearchAdapter
{
    public string Name => "Total Commander";

    public bool IsFileExplorer => true;

    private const string MainClass = "TTOTAL_CMD";

    // Total Commander's file-list panes carry a trailing number that varies with tree/FTP state, and the class
    // name differs by build: 32-bit TC uses "TMyListBox1/2", 64-bit TC uses "LCLListBox1/2" (Lazarus). Match
    // either by prefix -- an exact compare never hits and nothing would ever trigger.
    private static bool IsFileList(string className) =>
        className.StartsWith("TMyListBox", StringComparison.OrdinalIgnoreCase) ||
        className.StartsWith("LCLListBox", StringComparison.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (!PluginSettingsService.GetSetting("SwiftList.Plugins.TotalCommander", "EnableInlineSearch", true))
            return false;

        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName))
            return false;

        // TOTALCMD.EXE (32-bit) and TOTALCMD64.EXE both report a process name starting with "totalcmd".
        return processName.StartsWith("totalcmd", StringComparison.OrdinalIgnoreCase) &&
               className.Equals(MainClass, StringComparison.OrdinalIgnoreCase);
    }

    public bool CanTrigger(IntPtr focusedHwnd, string className)
    {
        if (focusedHwnd == IntPtr.Zero || string.IsNullOrEmpty(className))
            return false;

        // Only trigger from a file list, so typing in the command line / quick-rename box is left untouched.
        return IsFileList(className);
    }

    public bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor)
    {
        if (!PluginSettingsService.GetSetting("SwiftList.Plugins.TotalCommander", "EnableQuickNav", true))
            return false;

        return CanTrigger(hwndUnderCursor, classNameUnderCursor);
    }

    public string? GetSearchScope(IntPtr hwnd)
    {
        var path = Win32Helper.QuerySourcePanelPath(hwnd);
        if (string.IsNullOrEmpty(path)) return null;
        if (path.Length > 3 && path.EndsWith('\\'))
            path = path.TrimEnd('\\');
        return path;
    }

    public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
    {
        try
        {
            if (Directory.Exists(path))
            {
                // Enter the folder in the active pane.
                return Win32Helper.ChangeSourcePanelDirectory(hwnd, path, placeCursorOnItem: false);
            }
            if (File.Exists(path))
            {
                // Pass the file itself; the 'A' flag opens its parent folder and puts the cursor on it.
                return Win32Helper.ChangeSourcePanelDirectory(hwnd, path, placeCursorOnItem: true);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero) return false;

        // Prefer docking over the focused file list (the active pane).
        var focused = Win32Helper.GetFocusedControl(hwnd);
        if (focused != IntPtr.Zero &&
            IsFileList(Win32Helper.GetClassName(focused)) &&
            GetWindowRect(focused, out var fr))
        {
            rect = new AdapterRect { Left = fr.Left, Top = fr.Top, Right = fr.Right, Bottom = fr.Bottom };
            return true;
        }

        // Fall back to the whole lister. Extended frame bounds excludes the drop shadow, matching the visible edge.
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var dr, Marshal.SizeOf<RECT>()) == 0)
        {
            rect = new AdapterRect { Left = dr.Left, Top = dr.Top, Right = dr.Right, Bottom = dr.Bottom };
            return true;
        }

        if (GetWindowRect(hwnd, out var wr))
        {
            rect = new AdapterRect { Left = wr.Left, Top = wr.Top, Right = wr.Right, Bottom = wr.Bottom };
            return true;
        }

        return false;
    }

    public bool CanEnterActionsMode(IntPtr hwnd) => true;
}
