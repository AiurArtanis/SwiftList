using System.Diagnostics;
using System.Threading;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.QuickLookBridge;

// Moves QuickLook's own top-level window to a target rectangle via SetWindowPos -- no re-parenting, no
// style changes, so QuickLook's own window stays a completely normal top-level window as far as it (or
// Windows) knows. The only reason this needs to poll at all is that Invoke over the pipe is fire-and-
// forget: there's no signal for "the window now exists/updated," so DockTo just keeps checking for up to
// ~3s after being asked to reposition. QuickLook's own layout code re-centers the window on its own
// schedule too (e.g. right after it finishes rendering new content) -- expect it to occasionally win that
// race and leave the window slightly out of place until the next navigation re-asserts the dock.
internal static class QuickLookWindowPositioner
{
    private const int PollIntervalMs = 75;
    private const int MaxPollAttempts = 40; // ~3s

    private static readonly object Lock = new();
    private static IntPtr _lastKnownHwnd = IntPtr.Zero;
    private static Timer? _pollTimer;
    private static (int Left, int Top, int Width, int Height) _target;

    public static void DockTo(int left, int top, int width, int height)
    {
        lock (Lock)
        {
            _target = (left, top, width, height);

            if (_lastKnownHwnd != IntPtr.Zero && QuickLookDockInterop.IsWindow(_lastKnownHwnd))
            {
                Reposition(_lastKnownHwnd);
                return;
            }

            StartPollLocked();
        }
    }

    // Caller already holds Lock.
    private static void StartPollLocked()
    {
        _pollTimer?.Dispose();
        var attempts = 0;
        _pollTimer = new Timer(_ =>
        {
            attempts++;
            var pid = GetQuickLookProcessId();
            var hwnd = pid != 0 ? FindTopLevelWindow(pid) : IntPtr.Zero;

            if (hwnd != IntPtr.Zero)
            {
                lock (Lock)
                {
                    _lastKnownHwnd = hwnd;
                    Reposition(hwnd);
                    _pollTimer?.Dispose();
                    _pollTimer = null;
                }
                return;
            }

            if (attempts >= MaxPollAttempts)
            {
                Logger.Log("[QuickLookBridge] dock poll gave up: no QuickLook window found in time", LogLevel.Info);
                lock (Lock)
                {
                    _pollTimer?.Dispose();
                    _pollTimer = null;
                }
            }
        }, null, PollIntervalMs, PollIntervalMs);
    }

    // Caller already holds Lock.
    private static void Reposition(IntPtr hwnd)
    {
        try
        {
            QuickLookDockInterop.SetWindowPos(hwnd, IntPtr.Zero, _target.Left, _target.Top, _target.Width, _target.Height,
                QuickLookDockInterop.SWP_NOZORDER | QuickLookDockInterop.SWP_NOACTIVATE);
        }
        catch { }
    }

    private static int GetQuickLookProcessId()
    {
        var processes = Process.GetProcessesByName("QuickLook");
        try
        {
            return processes.Length > 0 ? processes[0].Id : 0;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static IntPtr FindTopLevelWindow(int processId)
    {
        var found = IntPtr.Zero;
        QuickLookDockInterop.EnumWindows((hwnd, _) =>
        {
            QuickLookDockInterop.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != processId || !QuickLookDockInterop.IsWindowVisible(hwnd))
                return true; // keep enumerating

            QuickLookDockInterop.GetWindowRect(hwnd, out var rect);
            if (rect.Right - rect.Left < 50 || rect.Bottom - rect.Top < 50)
                return true; // too small to be the viewer window (tray helper, tooltip, ...)

            found = hwnd;
            return false; // stop enumerating
        }, IntPtr.Zero);
        return found;
    }
}
