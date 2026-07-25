using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.Plugins.WindowSwitcher;

// Enumerates the same set of top-level windows the OS's own Alt+Tab switcher shows. The actual
// EnumWindows/DWM calls can't be unit tested without a live desktop, so the eligibility decision
// itself is pulled out into the pure IsAltTabEligible(...) method below -- that's what the tests
// exercise; this class is just the P/Invoke plumbing that gathers its inputs per window.
public static class WindowEnumerator
{
    public sealed record SwitchableWindow(IntPtr Handle, string Title, int ProcessId);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    private const uint GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int DWMWA_CLOAKED = 14;

    // The same combination the shell's own Alt+Tab switcher and every third-party task-switcher use:
    // visible, no owner (excludes most dialogs/tool popups, which are owned by their parent), not
    // cloaked (excludes UWP windows minimized to another virtual desktop, which report as visible even
    // though there's nothing to switch to), a real title, and not a tool window unless it also opts
    // back in via WS_EX_APPWINDOW (some apps set both).
    internal static bool IsAltTabEligible(bool isVisible, bool hasOwner, bool isCloaked, int titleLength, bool isToolWindow, bool isAppWindow)
    {
        if (!isVisible) return false;
        if (hasOwner) return false;
        if (isCloaked) return false;
        if (titleLength == 0) return false;
        if (isToolWindow && !isAppWindow) return false;
        return true;
    }

    // Excludes SwiftList's own windows (search window, settings, ...) -- switching to the window you
    // just picked this result from would be meaningless, and it's about to hide anyway.
    public static List<SwitchableWindow> GetSwitchableWindows()
    {
        var currentProcessId = Environment.ProcessId;
        var results = new List<SwitchableWindow>();

        EnumWindows((hWnd, _) =>
        {
            try
            {
                var isVisible = IsWindowVisible(hWnd);
                var hasOwner = GetWindow(hWnd, GW_OWNER) != IntPtr.Zero;
                var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                var isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
                var isAppWindow = (exStyle & WS_EX_APPWINDOW) != 0;
                var titleLength = GetWindowTextLength(hWnd);

                var isCloaked = false;
                try
                {
                    if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out var cloakedValue, sizeof(int)) == 0)
                        isCloaked = cloakedValue != 0;
                }
                catch { /* DWM unavailable -- treat as not cloaked */ }

                if (!IsAltTabEligible(isVisible, hasOwner, isCloaked, titleLength, isToolWindow, isAppWindow))
                    return true;

                GetWindowThreadProcessId(hWnd, out var pid);
                if (pid == currentProcessId)
                    return true;

                var sb = new StringBuilder(titleLength + 1);
                GetWindowText(hWnd, sb, sb.Capacity);

                results.Add(new SwitchableWindow(hWnd, sb.ToString(), (int)pid));
            }
            catch { /* skip a window that fails any of the above */ }

            return true;
        }, IntPtr.Zero);

        return results;
    }
}
