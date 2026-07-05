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
            if (sbClass.ToString().Contains("InputSwitch", StringComparison.OrdinalIgnoreCase)) return;

            GetWindowThreadProcessId(hwnd, out var activePid);
            if (activePid == (uint)Environment.ProcessId) return;

            _window.Dispatcher.BeginInvoke(new Action(() => { if (_window.IsVisible) HideWindow(); }), DispatcherPriority.Background);
        }
        catch { }
    }

    public static void ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SW_RESTORE);

        var fgHwnd = GetForegroundWindow();
        if (fgHwnd == hwnd) return;

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

            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _window.TxtSearch.Focus();
                System.Windows.Input.Keyboard.Focus(_window.TxtSearch);
            }), DispatcherPriority.Background);
        }), DispatcherPriority.Input);
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

        _window.ViewModel.SearchQuery = string.Empty;
        try
        {
            _window.ViewModel.Search.Results.Clear();
            _window.ViewModel.Search.SelectedResult = null;
        }
        catch { }

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
            try { Win32Api.TrimWorkingSet(); } catch { }
        });
    }
}
