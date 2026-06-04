using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SwiftList.App.Services;

namespace SwiftList.App.Views.QuickSearchWindow
{
    public class QuickSearchWindowController
    {
        private readonly SwiftList.App.QuickSearchWindow _window;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool fAttach);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        private const int SW_RESTORE = 9;
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0;

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private WinEventDelegate? _foregroundHookDelegate;
        private IntPtr _hForegroundHook = IntPtr.Zero;
        private IntPtr _lastActiveHwnd = IntPtr.Zero;

        private void StartForegroundHook()
        {
            if (_hForegroundHook != IntPtr.Zero) return;

            _foregroundHookDelegate = new WinEventDelegate(ForegroundEventProc);
            _hForegroundHook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _foregroundHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
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

        private void ForegroundEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero) return;

            try
            {
                // Ignore focus changes to Input Switch windows (like Shell_InputSwitchTopLevelWindow)
                var sbClass = new System.Text.StringBuilder(256);
                GetClassName(hwnd, sbClass, sbClass.Capacity);
                string cls = sbClass.ToString();
                if (cls.Contains("InputSwitch", System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Ignore focus changes to our own process's windows (e.g. QuickSearchWindow or InlineSearchWindow)
                GetWindowThreadProcessId(hwnd, out uint activePid);
                if (activePid == (uint)Environment.ProcessId)
                {
                    return;
                }

                // Any foreground switch to another process's window should hide us
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_window.IsVisible)
                    {
                        HideWindow();
                    }
                }), DispatcherPriority.Background);
            }
            catch { }
        }

        public static void ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            // Restore window if minimized, and make sure it is shown
            ShowWindow(hwnd, SW_RESTORE);

            IntPtr fgHwnd = GetForegroundWindow();
            if (fgHwnd == hwnd) return;

            uint fgThreadId = GetWindowThreadProcessId(fgHwnd, out _);
            uint appThreadId = GetCurrentThreadId();

            if (fgThreadId != appThreadId && fgThreadId != 0)
            {
                AttachThreadInput(appThreadId, fgThreadId, true);
                SetForegroundWindow(hwnd);
                SetActiveWindow(hwnd);
                SetFocus(hwnd);
                AttachThreadInput(appThreadId, fgThreadId, false);
            }
            else
            {
                SetForegroundWindow(hwnd);
                SetActiveWindow(hwnd);
                SetFocus(hwnd);
            }
        }

        public QuickSearchWindowController(SwiftList.App.QuickSearchWindow window)
        {
            _window = window;
        }

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
                ? System.Windows.Forms.Screen.FromHandle(_lastActiveHwnd)
                : System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition);

            var workingArea = screen.WorkingArea;
            _window.Left = (workingArea.Width * dpiScaleX - _window.Width) / 2 + workingArea.Left * dpiScaleX;
            _window.Top = workingArea.Height * dpiScaleY * 0.25 + workingArea.Top * dpiScaleY;
        }

        public void ToggleVisibility()
        {
            _window.Dispatcher.Invoke(() =>
            {
                if (_window.IsVisible && _window.WindowState != WindowState.Minimized)
                {
                    HideWindow();
                }
                else
                {
                    ShowWindow();
                }
            });
        }

        public void ShowWindow(string? initialQuery = null)
        {
            // Capture the currently active window before we show up and steal focus
            _lastActiveHwnd = GetForegroundWindow();
            if (_lastActiveHwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(_lastActiveHwnd, out uint activePid);
                if (activePid == (uint)Environment.ProcessId)
                {
                    _lastActiveHwnd = IntPtr.Zero;
                }
            }

            _window.ViewModel.IsInlineSearchContext = false;

            // Hide and clear inline search window data
            App.HideInlineSearch();

            // Disable inline keyboard hook completely while the quick window owns input.
            InlineSearchManager.Instance.KeyboardHook.IsQuickSearchWindowVisible = true;
            InlineSearchManager.Instance.KeyboardHook.Stop();

            _window.ViewModel.SearchQuery = initialQuery ?? string.Empty;

            _window.UpdateLayout();

            _window.Topmost = false;
            _window.Topmost = true;
            _window.Show();
            _window.WindowState = WindowState.Normal;
            PositionWindow();

            _window.ViewModel.TriggerIndexBuild();

            // Start foreground active window monitoring hook to automatically hide when clicking away
            StartForegroundHook();

            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _window.Activate();
                _window.Focus();
                
                var hwnd = new WindowInteropHelper(_window).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    ForceForeground(hwnd);
                }

                _window.TxtSearch.Focus();
                System.Windows.Input.Keyboard.Focus(_window.TxtSearch);
            }), DispatcherPriority.Input);
        }

        public void HideWindow(bool restoreFocus = true)
        {
            // Stop foreground window hook
            StopForegroundHook();

            if (_window.MenuPresenter != null && _window.MenuPresenter.IsInActionsMode)
            {
                _window.MenuPresenter.ExitActionsMode();
            }
            
            _window.ViewModel.SearchQuery = string.Empty;
            try
            {
                _window.ViewModel.Search.Results.Clear();
                _window.ViewModel.Search.SelectedResult = null;
            }
            catch { }

            _window.UpdateLayout();

            _window.Hide();

            // Re-enable inline keyboard hook when the quick window no longer owns input.
            InlineSearchManager.Instance.KeyboardHook.IsQuickSearchWindowVisible = false;
            InlineSearchManager.Instance.KeyboardHook.Start();

            // Restore focus to the previously active window
            if (restoreFocus && _lastActiveHwnd != IntPtr.Zero)
            {
                SetForegroundWindow(_lastActiveHwnd);
            }
            _lastActiveHwnd = IntPtr.Zero;

            // Trim working set in the background after transition
            System.Threading.Tasks.Task.Run(() =>
            {
                System.Threading.Thread.Sleep(100);
                try
                {
                    SwiftList.Core.Win32Api.TrimWorkingSet();
                }
                catch { }
            });
        }
    }
}
