using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SwiftList.App.Services;
using SwiftList.Core;

namespace SwiftList.App.Views.QuickSearchWindow;

public class QuickSearchWindowController
{
    private readonly SwiftList.App.QuickSearchWindow _window;

    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const int SW_RESTORE = 9;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
    private WinEventDelegate? _foregroundHookDelegate;
    private IntPtr _hForegroundHook = IntPtr.Zero;
    private IntPtr _lastActiveHwnd = IntPtr.Zero;

    private void StartForegroundHook()
    {
        if (_hForegroundHook != IntPtr.Zero) return;
        _foregroundHookDelegate = ForegroundEventProc;
        _hForegroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _foregroundHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
    }
    private void StopForegroundHook()
    {
        if (_hForegroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_hForegroundHook);
            _hForegroundHook = IntPtr.Zero;
        }
        _foregroundHookDelegate = null;
    }
    private void ForegroundEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var sbClass = new StringBuilder(256);
            GetClassName(hwnd, sbClass, sbClass.Capacity);
            var className = sbClass.ToString();
            GetWindowThreadProcessId(hwnd, out var activePid);
            var procName = StartMenuDismissHelper.TryGetProcessName(activePid);

            // TODO(issue #68): temporary diagnostic for "a system notification makes the search window
            // disappear" -- couldn't reproduce with a plain WinRT toast fired under Explorer's AUMID, so
            // logging every candidate here (skipped or not) to see what's actually triggering it for the
            // reporter. Remove once root-caused.
            Logger.Log($"[ForegroundHook] class='{className}' pid={activePid} proc='{procName}'", LogLevel.Info);

            // A preview provider may be hosting an out-of-process native handler (e.g. Office acting as
            // its own Preview Handler COM server), whose window can grab foreground on its own -- at
            // startup or from interacting with its content (e.g. a right-click menu) -- for as long as
            // it's shown. See PreviewActivationSignal. That isn't the user switching to another app.
            if (PluginSdk.Services.PreviewActivationSignal.IsActive) return;

            if (className.Contains("InputSwitch", StringComparison.OrdinalIgnoreCase)) return;

            if (activePid == (uint)Environment.ProcessId) return;

            // A transient foreground steal can happen mid-typing without the user actually switching away
            // -- e.g. rendering a \\wsl$ result's icon/modified date wakes the WSL VM, whose cold start
            // briefly flashes a console host that grabs foreground (see the identical wait-and-recheck in
            // QuickSearchWindow.Window_Deactivated, added for the same reason). That path alone isn't
            // enough: this hook fires independently and used to hide immediately on the very first event.
            // Debounce here too, so foreground that bounces right back doesn't drop the search window.
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_window.IsVisible) return;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                timer.Tick += (s, _) =>
                {
                    timer.Stop();
                    if (!_window.IsVisible) return;
                    GetWindowThreadProcessId(GetForegroundWindow(), out var stillActivePid);
                    if (stillActivePid == (uint)Environment.ProcessId) return;
                    HideWindow();
                };
                timer.Start();
            }), DispatcherPriority.Background);
        }
        catch { }
    }

    public static void ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SW_RESTORE);

        // Used to skip the IPC round-trip when GetForegroundWindow() already reported hwnd as
        // foreground, on the assumption that Show()/Activate() already did the whole job. That
        // assumption isn't always safe (e.g. a still-open Start Menu can make Windows report our
        // window as foreground without real per-thread keyboard focus having actually moved -- see
        // StartMenuDismissHelper.DismissStartMenuIfOpen(), which handles that specific case earlier in
        // ShowWindow()). Always send it regardless; redoing an already-correct foreground/focus state
        // is cheap and harmless.
        App.HookClient?.SendMessage(new IpcMessage
        {
            Id = IpcMessageId.ForceForeground,
            Hwnd = hwnd.ToInt64()
        });
    }

    public QuickSearchWindowController(SwiftList.App.QuickSearchWindow window) => _window = window;

    public void PositionWindow()
    {
        double dpiScaleX = 1.0, dpiScaleY = 1.0;
        var source = PresentationSource.FromVisual(_window);
        if (source?.CompositionTarget != null)
        {
            dpiScaleX = source.CompositionTarget.TransformFromDevice.M11;
            dpiScaleY = source.CompositionTarget.TransformFromDevice.M22;
        }

        var screen = _lastActiveHwnd != IntPtr.Zero
            ? Screen.FromHandle(_lastActiveHwnd)
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
            _window.Top = workingArea.Height * dpiScaleY * 0.25 + workingArea.Top * dpiScaleY;
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

    public void ToggleVisibility() => _window.Dispatcher.Invoke(() =>
                                           {
                                               if (_window.IsVisible && _window.WindowState != WindowState.Minimized) HideWindow();
                                               else ShowWindow();
                                           });

    public void ShowWindow(string? initialQuery = null)
    {
        // Must run before anything below touches this window (Show()/Activate()/ForceForeground):
        // once any of those runs, GetForegroundWindow() starts reporting THIS window as foreground
        // (the Start Menu doesn't compete for activation the normal way), so this is the last point
        // where it still reflects the real, uncontaminated state.
        StartMenuDismissHelper.DismissStartMenuIfOpen();

        _lastActiveHwnd = GetForegroundWindow();
        if (_lastActiveHwnd != IntPtr.Zero)
        {
            GetWindowThreadProcessId(_lastActiveHwnd, out var activePid);
            if (activePid == (uint)Environment.ProcessId) _lastActiveHwnd = IntPtr.Zero;
        }

        _window.ViewModel.IsInlineSearchContext = false;
        App.HideInlineSearch();
        QuickLookManager.Instance.Reset();

        InlineSearchManager.Instance.KeyboardHook.IsQuickSearchWindowVisible = true;
        InlineSearchManager.Instance.KeyboardHook.Stop();
        _window.ViewModel.EnsureServiceMonitoringActive();

        _window.ViewModel.SearchQuery = initialQuery ?? string.Empty;
        _window.ViewModel.RefreshEmptyState();
        _window.ViewModel.RefreshLayoutSettings();
        _window.UpdateLayout();
        _window.Topmost = false;
        _window.Topmost = true;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        PositionWindow();

        StartForegroundHook();

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd != IntPtr.Zero) ForceForeground(hwnd);

            _window.Activate();
            _window.Focus();

            FocusSearchBoxWhenForeground(hwnd);
        }), DispatcherPriority.Input);
    }

    // ForceForeground's SetForegroundWindow call -- whether it succeeds locally or has to round-trip
    // through the elevated Hook process's IPC -- doesn't complete synchronously with the call that
    // requested it. TxtSearch.Focus() used to fire after a single fixed-priority dispatcher hop, a
    // guess at "enough time has probably passed" that could still land before the OS actually handed
    // this window real keyboard focus, silently dropping any keys the user typed in that gap right
    // after invoking the hotkey (see issue #121). Poll the real OS state instead: 10ms ticks, capped
    // at 200ms so a case where foreground genuinely never arrives (something else is holding it,
    // blocked by Windows' foreground-lock rules) still ends in focusing the search box rather than
    // leaving it silently unfocused forever.
    private void FocusSearchBoxWhenForeground(IntPtr hwnd)
    {
        var deadline = Environment.TickCount64 + 200;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (s, _) =>
        {
            var isForeground = hwnd == IntPtr.Zero || GetForegroundWindow() == hwnd;
            if (!isForeground && Environment.TickCount64 < deadline)
                return;

            timer.Stop();
            _window.TxtSearch.Focus();
            System.Windows.Input.Keyboard.Focus(_window.TxtSearch);
        };
        timer.Start();
    }

    public void HideWindow(bool restoreFocus = true)
    {
        StopForegroundHook();
        if (_window.MenuPresenter != null && _window.MenuPresenter.IsInActionsMode)
        {
            _window.MenuPresenter.ExitActionsMode();
        }

        _window.ViewModel.Monitor.StopStatusTimer();

        try { KeywordHistoryStore.Record(_window.ViewModel.SearchQuery); } catch { }
        _window.KeywordHistoryController.Reset();

        // SearchQuery's own setter already runs PerformSearch("") when this actually changes the query
        // (clearing/replacing results the normal way). Explicitly wiping Search.Results here on top of
        // that used to erase the startup panel's own still-valid results/tabs the moment the box was
        // already empty (nothing "changes" so the setter is a no-op) -- meaning next time the window
        // showed, there was nothing left to display while the panel's async refetch ran, which is what
        // produced the empty/loading flash ShowWindow's RefreshEmptyState() was supposed to avoid.
        _window.ViewModel.SearchQuery = string.Empty;

        _window.UpdateLayout();
        _window.Hide();

        InlineSearchManager.Instance.KeyboardHook.IsQuickSearchWindowVisible = false;
        InlineSearchManager.Instance.KeyboardHook.Start();

        if (restoreFocus && _lastActiveHwnd != IntPtr.Zero) SetForegroundWindow(_lastActiveHwnd);
        _lastActiveHwnd = IntPtr.Zero;

        Task.Run(async () =>
        {
            await Task.Delay(100);
            try { ShellIconHelper.ClearCache(); } catch { }
            try { PathCacheMaintenance.ClearAllPathCaches(); } catch { }
            try { Win32Api.TrimWorkingSet(); } catch { }
        });
    }
}
