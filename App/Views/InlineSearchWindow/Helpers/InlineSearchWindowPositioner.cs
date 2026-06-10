using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SwiftList.App.Services;
using SwiftList.PluginSdk;

namespace SwiftList.App.Views.InlineSearchWindow.Helpers
{
    public class InlineSearchWindowPositioner
    {
        private readonly SwiftList.App.InlineSearchWindow _window;
        private int _positionUpdateQueued;

        public InlineSearchWindowPositioner(SwiftList.App.InlineSearchWindow window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        public void PositionWindow()
        {
            if (Interlocked.Exchange(ref _positionUpdateQueued, 1) == 1)
                return;

            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _positionUpdateQueued, 0);
                if (_window.IsVisible)
                    PositionWindowCore();
            }), DispatcherPriority.Render);
        }

        private void PositionWindowCore()
        {
            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;
            var source = PresentationSource.FromVisual(_window);
            if (source != null && source.CompositionTarget != null)
            {
                dpiScaleX = source.CompositionTarget.TransformFromDevice.M11;
                dpiScaleY = source.CompositionTarget.TransformFromDevice.M22;
            }
            else
            {
                try
                {
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(_window);
                    dpiScaleX = 1.0 / dpi.DpiScaleX;
                    dpiScaleY = 1.0 / dpi.DpiScaleY;
                }
                catch
                {
                    // Fallback
                }
            }

            double windowHeight = _window.ActualHeight > 0 ? _window.ActualHeight : 60;
            double windowWidth = _window.Width;

            // MainBorder in XAML has Margin="12" to make room for drop shadow.
            // We want the visible border to be exactly aligned to the screen/window corner.
            const double xamlMargin = 12;
            const double visibleMargin = 0;

            var tracker = _window.Manager.ExplorerTracker;

            if (tracker.IsDesktop)
            {
                // Standard layout: Results on top (Row 0), Search Box on bottom (Row 2)
                Grid.SetRow(_window.ResultsPanelControl, 0);
                Grid.SetRow(_window.ResultsSeparator, 1);
                Grid.SetRow(_window.SearchBoxBorder, 2);

                var screen = System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0];
                var workingArea = screen.WorkingArea;
                _window.Left = workingArea.Right * dpiScaleX - windowWidth + xamlMargin - visibleMargin;
                _window.Top = workingArea.Bottom * dpiScaleY - windowHeight + xamlMargin - visibleMargin;
            }
            else if (tracker.ActiveHwnd != IntPtr.Zero)
            {
                SwiftList.Core.Hook.ExplorerTracker.RECT rect;
                // Check if TryGetActiveWindowRect succeeds AND returns a valid non-empty window size (width and height > 100)
                bool hasValidRect = tracker.TryGetActiveWindowRect(out rect) && (rect.Right - rect.Left > 100 && rect.Bottom - rect.Top > 100);

                if (hasValidRect)
                {
                    double winLeft = rect.Left * dpiScaleX;
                    double winTop = rect.Top * dpiScaleY;
                    double winRight = rect.Right * dpiScaleX;
                    double winBottom = rect.Bottom * dpiScaleY;

                    if (tracker.IsActiveWindowDialog)
                    {
                        // Swap layout: Search Box on top (Row 0), Results on bottom (Row 2)
                        Grid.SetRow(_window.SearchBoxBorder, 0);
                        Grid.SetRow(_window.ResultsSeparator, 1);
                        Grid.SetRow(_window.ResultsPanelControl, 2);

                        double winWidth = (rect.Right - rect.Left) * dpiScaleX;
                        _window.Left = winLeft + (winWidth - windowWidth) / 2;
                        // Align top of search window to bottom of dialog
                        _window.Top = winBottom - xamlMargin + visibleMargin;
                    }
                    else
                    {
                        // Standard layout: Results on top (Row 0), Search Box on bottom (Row 2)
                        Grid.SetRow(_window.ResultsPanelControl, 0);
                        Grid.SetRow(_window.ResultsSeparator, 1);
                        Grid.SetRow(_window.SearchBoxBorder, 2);

                        _window.Left = winRight - windowWidth + xamlMargin - visibleMargin;
                        _window.Top = winBottom - windowHeight + xamlMargin - visibleMargin;
                    }

                    // Constrain within the monitor work area where the active window is located
                    var screen = System.Windows.Forms.Screen.FromHandle(tracker.ActiveHwnd);
                    var workingArea = screen.WorkingArea;
                    double minLeft = workingArea.Left * dpiScaleX + visibleMargin - xamlMargin;
                    double minTop = workingArea.Top * dpiScaleY + visibleMargin - xamlMargin;
                    double maxLeft = workingArea.Right * dpiScaleX - windowWidth + xamlMargin - visibleMargin;
                    double maxTop = workingArea.Bottom * dpiScaleY - windowHeight + xamlMargin - visibleMargin;

                    if (_window.Left < minLeft) _window.Left = minLeft;
                    if (_window.Top < minTop) _window.Top = minTop;
                    if (_window.Left > maxLeft) _window.Left = maxLeft;
                    if (_window.Top > maxTop) _window.Top = maxTop;
                }
                else
                {
                    // Fallback: place the window safely in the bottom-right corner of the active window's monitor work area so it is fully visible
                    var screen = tracker.ActiveHwnd != IntPtr.Zero
                        ? System.Windows.Forms.Screen.FromHandle(tracker.ActiveHwnd)
                        : System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0];
                    var workingArea = screen.WorkingArea;
                    _window.Left = workingArea.Right * dpiScaleX - windowWidth + xamlMargin - visibleMargin;
                    _window.Top = workingArea.Bottom * dpiScaleY - windowHeight + xamlMargin - visibleMargin;
                }
            }
        }
    }
}
