using System.Runtime.InteropServices;
using System.Windows.Threading;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Preview;

// Recovery layer for a native preview handler's own out-of-process window -- e.g. Excel, running as its
// own prevhost surrogate rather than a thin preview-only handler -- stealing OS keyboard focus away from
// the host app once its content finishes initializing. PreviewHandlerHost.WndProc handles prevention
// (WM_MOUSEACTIVATE/WM_SETFOCUS on _hostHwnd itself); this class is the fallback for whatever slips past
// that, e.g. an asynchronous, non-click-driven SetFocus a handler issues on its own schedule.
//
// Tried and reverted here: severing the child's input-queue attachment via AttachThreadInput(..., false)
// on WM_PARENTNOTIFY, hoping to stop the steal before it happens. It backfired -- the child's implicit
// attachment to our queue is apparently what keeps its clicks from triggering a REAL OS-level foreground
// switch to its own top-level window; detaching it made clicking into the preview activate that window
// for real, which cascaded into QuickLookManager.Owner_Deactivated treating it as focus lost to another
// app entirely and closing the whole search window.
//
// A bounded, PID-scoped EVENT_OBJECT_FOCUS watcher: reacts to the real focus-change event (not a guessed
// delay) and reports back via PreviewActivationSignal so the host can reclaim focus.
internal sealed class PreviewFocusGuard
{
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private const uint EVENT_OBJECT_FOCUS = 0x8005;
    private const uint WINEVENT_OUTOFCONTEXT = 0;

    private WinEventDelegate? _focusHookDelegate;
    private IntPtr _hFocusHook;
    private DispatcherTimer? _focusGraceTimer;

    // Called on WM_PARENTNOTIFY(WM_CREATE) for the host window -- the earliest reliable signal that the
    // handler's rendering window (possibly cross-process) has been attached as our child, so its PID is
    // known before GrantForegroundRights (which runs later, after DoPreview returns) would otherwise
    // resolve it.
    public void OnChildWindowCreated(IntPtr childHwnd)
    {
        if (childHwnd == IntPtr.Zero) return;
        try
        {
            PreviewHandlerInterop.GetWindowThreadProcessId(childHwnd, out var pid);
            if (pid != 0) ArmFallbackDetector(pid);
        }
        catch { }
    }

    // Some handlers (Excel especially) can still grab focus asynchronously, on their own schedule --
    // anywhere from tens of milliseconds to well over a second later depending on the file -- despite
    // PreviewHandlerHost's WM_MOUSEACTIVATE/WM_SETFOCUS prevention. Reacting to the real focus-change
    // event instead of guessing a fixed delay catches it exactly when it happens, whatever that timing
    // turns out to be. Scoped to the specific handler PID (an unrelated app the user switches to isn't
    // mistaken for a steal) and to a bounded grace window after load (so a later, deliberate click into
    // the preview -- scrolling, playback controls -- is left alone instead of being fought).
    private void ArmFallbackDetector(uint pid)
    {
        DisarmFallbackDetector();
        _focusHookDelegate = (h, evt, hwnd, idObject, idChild, thread, time) =>
        {
            if (hwnd == IntPtr.Zero) return;
            PreviewHandlerInterop.GetWindowThreadProcessId(hwnd, out var focusedPid);
            if (focusedPid != pid) return;
            DisarmFallbackDetector();
            PreviewActivationSignal.NotifyFocusStolen();
        };
        _hFocusHook = SetWinEventHook(EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS, IntPtr.Zero, _focusHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        _focusGraceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _focusGraceTimer.Tick += (s, e) => DisarmFallbackDetector();
        _focusGraceTimer.Start();
    }

    private void DisarmFallbackDetector()
    {
        if (_hFocusHook != IntPtr.Zero)
        {
            UnhookWinEvent(_hFocusHook);
            _hFocusHook = IntPtr.Zero;
        }
        _focusHookDelegate = null;
        _focusGraceTimer?.Stop();
        _focusGraceTimer = null;
    }

    public void Dispose() => DisarmFallbackDetector();
}
