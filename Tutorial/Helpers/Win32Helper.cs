using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.Tutorial.Helpers;

public static class Win32Helper
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // ponytail: Find search text by enumerating edit controls via pure Win32.
    // Ceiling: Returns text from the first child window containing "Edit" in class name.
    public static string GetForegroundSearchText(IntPtr hWnd)
    {
        var result = "";
        EnumChildWindows(hWnd, (childHwnd, lParam) =>
        {
            var sbClass = new StringBuilder(256);
            GetClassName(childHwnd, sbClass, sbClass.Capacity);
            var clsName = sbClass.ToString();
            if (clsName.Contains("Edit", StringComparison.OrdinalIgnoreCase))
            {
                var sbText = new StringBuilder(1024);
                SendMessage(childHwnd, 0x000D /* WM_GETTEXT */, (IntPtr)sbText.Capacity, sbText);
                result = sbText.ToString();
                return false; // Stop enumeration
            }
            return true;
        }, IntPtr.Zero);

        return result;
    }

    // ponytail: Check if actions menu is open by looking for standard Win32 menu window.
    // Ceiling: Checks global active class #32768 menu visibility.
    public static bool IsActionsMenuOpen(IntPtr hWnd)
    {
        var menuHwnd = FindWindow("#32768", null);
        return menuHwnd != IntPtr.Zero && IsWindowVisible(menuHwnd);
    }

    public static string GetProcessNameFromWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "";
        GetWindowThreadProcessId(hWnd, out var pid);
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return "";
        }
    }
}
