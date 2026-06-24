using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SwiftList.App.Views.InlineSearchWindow.Helpers;

public class InlineSearchWindowPositioner
{
    private readonly SwiftList.App.InlineSearchWindow _window;
    private int _positionUpdateQueued;

    public InlineSearchWindowPositioner(SwiftList.App.InlineSearchWindow window) => _window = window ?? throw new ArgumentNullException(nameof(window));

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
        _window.UpdateLayout();
        var dpiScaleX = 1.0;
        var dpiScaleY = 1.0;
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

        var windowHeight = _window.ActualHeight > 0 ? _window.ActualHeight : 60;
        var windowWidth = _window.Width;

        // MainBorder in XAML has Margin="12" to make room for drop shadow.
        // We want the visible border to be exactly aligned to the screen/window corner.
        const double xamlMargin = 12;
        const double visibleMargin = 0;

        var tracker = _window.Manager.ExplorerTracker;
        var isResultsVisible = _window.ResultsPanelControl.Visibility == Visibility.Visible;

        // Default layout: results on top, search box on bottom with rounded corners
        Grid.SetRow(_window.ResultsContainerWrapper, 0);
        Grid.SetRow(_window.ResultsSeparator, 1);
        Grid.SetRow(_window.SearchBoxBorder, 2);
        Grid.SetRow(_window.PathPreviewBorder, 0);
        Grid.SetRow(_window.ResultsPanelControl, 1);
        _window.PathPreviewBorder.BorderThickness = new Thickness(0, 0, 0, 1);
        _window.MainBorder.CornerRadius = new CornerRadius(8);
        _window.SearchBoxBorder.CornerRadius = isResultsVisible ? new CornerRadius(0, 0, 7, 7) : new CornerRadius(7);

        if (tracker.IsDesktop)
        {
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var workingArea = screen.WorkingArea;
            var targetLeft = workingArea.Right * dpiScaleX - windowWidth + xamlMargin - visibleMargin;
            var targetTop = workingArea.Bottom * dpiScaleY - windowHeight + xamlMargin - visibleMargin;

            if (Math.Abs(_window.Left - targetLeft) > 0.5) _window.Left = targetLeft;
            if (Math.Abs(_window.Top - targetTop) > 0.5) _window.Top = targetTop;
        }
        else if (tracker.ActiveHwnd != IntPtr.Zero)
        {
            // Check if TryGetActiveWindowRect succeeds AND returns a valid non-empty window size (width and height > 100)
            var hasValidRect = tracker.TryGetActiveWindowRect(out var rect) && (rect.Right - rect.Left > 100 && rect.Bottom - rect.Top > 100);

            if (hasValidRect)
            {
                var winLeft = rect.Left * dpiScaleX;
                var winTop = rect.Top * dpiScaleY;
                var winRight = rect.Right * dpiScaleX;
                var winBottom = rect.Bottom * dpiScaleY;

                double targetLeft = 0;
                double targetTop = 0;

                if (tracker.IsActiveWindowDialog)
                {
                    // Swap layout: Search Box on top (Row 0), Results on bottom (Row 2)
                    Grid.SetRow(_window.SearchBoxBorder, 0);
                    Grid.SetRow(_window.ResultsSeparator, 1);
                    Grid.SetRow(_window.ResultsContainerWrapper, 2);
                    Grid.SetRow(_window.PathPreviewBorder, 1);
                    Grid.SetRow(_window.ResultsPanelControl, 0);
                    _window.PathPreviewBorder.BorderThickness = new Thickness(0, 1, 0, 0);
                    _window.MainBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);
                    _window.SearchBoxBorder.CornerRadius = isResultsVisible ? new CornerRadius(0) : new CornerRadius(0, 0, 7, 7);

                    var winWidth = (rect.Right - rect.Left) * dpiScaleX;
                    targetLeft = winLeft + (winWidth - windowWidth) / 2;
                    // Align top of search window to bottom of dialog
                    targetTop = winBottom - xamlMargin + visibleMargin;
                }
                else
                {
                    targetLeft = winRight - windowWidth + xamlMargin - visibleMargin;
                    targetTop = winBottom - windowHeight + xamlMargin - visibleMargin;
                }

                // Constrain within the monitor work area where the active window is located
                var screen = Screen.FromHandle(tracker.ActiveHwnd);
                var workingArea = screen.WorkingArea;
                var minLeft = workingArea.Left * dpiScaleX + visibleMargin - xamlMargin;
                var minTop = workingArea.Top * dpiScaleY + visibleMargin - xamlMargin;
                var maxLeft = workingArea.Right * dpiScaleX - windowWidth + xamlMargin - visibleMargin;
                var maxTop = workingArea.Bottom * dpiScaleY - windowHeight + xamlMargin - visibleMargin;

                if (targetLeft < minLeft) targetLeft = minLeft;
                if (targetTop < minTop) targetTop = minTop;
                if (targetLeft > maxLeft) targetLeft = maxLeft;
                if (targetTop > maxTop) targetTop = maxTop;

                if (Math.Abs(_window.Left - targetLeft) > 0.5) _window.Left = targetLeft;
                if (Math.Abs(_window.Top - targetTop) > 0.5) _window.Top = targetTop;
            }
            else
            {
                // Fallback: place the window safely in the bottom-right corner of the active window's monitor work area so it is fully visible
                var screen = tracker.ActiveHwnd != IntPtr.Zero
                    ? Screen.FromHandle(tracker.ActiveHwnd)
                    : Screen.PrimaryScreen ?? Screen.AllScreens[0];
                var workingArea = screen.WorkingArea;
                var targetLeft = workingArea.Right * dpiScaleX - windowWidth + xamlMargin - visibleMargin;
                var targetTop = workingArea.Bottom * dpiScaleY - windowHeight + xamlMargin - visibleMargin;

                if (Math.Abs(_window.Left - targetLeft) > 0.5) _window.Left = targetLeft;
                if (Math.Abs(_window.Top - targetTop) > 0.5) _window.Top = targetTop;
            }
        }
    }
}
