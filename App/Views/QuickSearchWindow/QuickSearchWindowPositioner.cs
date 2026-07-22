using System.Runtime.InteropServices;
using SwiftList.Core;

namespace SwiftList.App.Views.QuickSearchWindow;

public class QuickSearchWindowPositioner
{
    [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private readonly SwiftList.App.QuickSearchWindow _window;
    private readonly Func<IntPtr> _getLastActiveHwnd;

    public QuickSearchWindowPositioner(SwiftList.App.QuickSearchWindow window, Func<IntPtr> getLastActiveHwnd)
    {
        _window = window;
        _getLastActiveHwnd = getLastActiveHwnd;
    }

    public void PositionWindow()
    {
        var lastActiveHwnd = _getLastActiveHwnd();

        // DPI must come from the monitor this window is about to be placed ON, not from wherever it
        // currently happens to sit (PresentationSource.FromVisual(_window)'s own CompositionTarget, the
        // old source here) -- see InlineSearchWindowPositioner's identical fix for the full writeup.
        // This window persists for the whole app session (only ever Hidden, never Closed), so it can be
        // sitting on whatever monitor it was last shown on when ShowWindow() runs again for a different
        // (differently-scaled) monitor; on a mixed-DPI multi-monitor setup that stale source computes a
        // position wrong by exactly the ratio between the two monitors' scales.
        var targetMonitor = lastActiveHwnd != IntPtr.Zero
            ? MonitorFromWindow(lastActiveHwnd, MONITOR_DEFAULTTONEAREST)
            : MonitorFromPoint(new POINT { X = Control.MousePosition.X, Y = Control.MousePosition.Y }, MONITOR_DEFAULTTONEAREST);
        var (dpiScaleX, dpiScaleY) = GetMonitorDpiScale(targetMonitor);

        var screen = lastActiveHwnd != IntPtr.Zero
            ? Screen.FromHandle(lastActiveHwnd)
            : Screen.FromPoint(Control.MousePosition);

        var workingArea = screen.WorkingArea;
        var settings = UserSettings.Load();
        var windowWidth = settings.SearchWindow.SearchBarWidth + 48;
        if (settings.SearchWindow.Left.HasValue && settings.SearchWindow.Top.HasValue
            && IsAnchorOnAnyScreen(settings.SearchWindow.Left.Value + windowWidth / 2, settings.SearchWindow.Top.Value + 20, dpiScaleX, dpiScaleY))
        {
            // A saved position may point at a monitor that has since been unplugged or resized, which would
            // open the window off-screen where it can't be seen or reached. Only restore it when its top
            // strip still lands on a connected monitor's work area; otherwise fall back to centering below.
            _window.Left = settings.SearchWindow.Left.Value;
            _window.Top = settings.SearchWindow.Top.Value;
        }
        else
        {
            _window.Left = (workingArea.Width * dpiScaleX - windowWidth) / 2 + workingArea.Left * dpiScaleX;
            _window.Top = workingArea.Height * dpiScaleY * 0.22 + workingArea.Top * dpiScaleY;
        }
    }

    // Wired to the search box's status icon right-click -- clears the saved position and immediately
    // re-centers the window using the same fallback PositionWindow already falls back to when there's
    // no saved position (or it's off-screen).
    public void ResetPosition()
    {
        var settings = UserSettings.Load();
        settings.SearchWindow.Left = null;
        settings.SearchWindow.Top = null;
        settings.Save();
        PositionWindow();
    }

    // Falls back to 1.0 (96 DPI, unscaled) if the monitor handle is invalid or the query fails --
    // GetDpiForMonitor has been available since Windows 8.1, so this should only trip on some
    // unexpected edge case, not any supported OS version.
    private static (double x, double y) GetMonitorDpiScale(IntPtr hMonitor)
    {
        if (hMonitor != IntPtr.Zero && GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
            return (96.0 / dpiX, 96.0 / dpiY);
        return (1.0, 1.0);
    }

    // Saved Left/Top are DIP; Screen work areas are physical (system-DPI space), so scale them to DIP
    // with the same factor before testing whether the given DIP anchor point falls on any monitor.
    private static bool IsAnchorOnAnyScreen(double anchorX, double anchorY, double dpiScaleX, double dpiScaleY)
    {
        foreach (var s in Screen.AllScreens)
        {
            var wa = s.WorkingArea;
            if (anchorX >= wa.Left * dpiScaleX && anchorX <= wa.Right * dpiScaleX &&
                anchorY >= wa.Top * dpiScaleY && anchorY <= wa.Bottom * dpiScaleY)
            {
                return true;
            }
        }
        return false;
    }
}
