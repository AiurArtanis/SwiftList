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

        // Window height is fixed at 550 logical pixels to allow content to grow upwards/downwards internally
        var windowHeight = double.IsNaN(_window.Height) || _window.Height <= 0 
            ? (_window.ActualHeight > 0 ? _window.ActualHeight : 550.0) 
            : _window.Height;
        var windowWidth = _window.Width;

        // Get actual visible content height (MainBorder)
        var visibleHeight = _window.MainBorder.ActualHeight > 0 
            ? _window.MainBorder.ActualHeight 
            : windowHeight;

        // MainBorder in XAML has Margin="12" to make room for drop shadow.
        // We want the visible border to be exactly aligned to the screen/window corner.
        const double xamlMargin = 12;
        const double visibleMargin = 0;

        var tracker = _window.Manager.ExplorerTracker;
        var isResultsVisible = _window.ResultsPanelControl.Visibility == Visibility.Visible;

        var useDialogMode = false;
        var hasValidRect = false;
        var rect = new Core.Hook.ExplorerTracker.RECT();

        if (tracker.ActiveHwnd != IntPtr.Zero && !tracker.IsDesktop)
        {
            hasValidRect = tracker.TryGetActiveWindowRect(out rect) && (rect.Right - rect.Left > 100 && rect.Bottom - rect.Top > 100);
            if (hasValidRect && tracker.IsActiveWindowDialog)
            {
                var screen = Screen.FromHandle(tracker.ActiveHwnd);
                var workingArea = screen.WorkingArea;
                var winBottom = rect.Bottom * dpiScaleY;
                var spaceBelow = (workingArea.Bottom * dpiScaleY) - winBottom;
                if (spaceBelow >= (visibleHeight - xamlMargin))
                {
                    useDialogMode = true;
                }
            }
        }

        if (useDialogMode)
        {
            // Dialog mode: search box on top (Row 0), results on bottom (Row 2), content aligns to top of transparent window
            _window.RootGrid.VerticalAlignment = VerticalAlignment.Top;
            _window.MainBorder.VerticalAlignment = VerticalAlignment.Top;

            Grid.SetRow(_window.SearchBoxBorder, 0);
            Grid.SetRow(_window.ResultsSeparator, 1);
            Grid.SetRow(_window.ResultsContainerWrapper, 2);
            Grid.SetRow(_window.PathPreviewBorder, 1);
            Grid.SetRow(_window.ResultsPanelControl, 0);
            _window.PathPreviewBorder.BorderThickness = new Thickness(0, 1, 0, 0);
            _window.PathPreviewBorder.CornerRadius = new CornerRadius(0, 0, 7, 7);
            _window.MainBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);
            _window.SearchBoxBorder.CornerRadius = isResultsVisible ? new CornerRadius(0) : new CornerRadius(0, 0, 7, 7);
        }
        else
        {
            // Standard mode: results on top, search box on bottom, content aligns to bottom of transparent window
            _window.RootGrid.VerticalAlignment = VerticalAlignment.Bottom;
            _window.MainBorder.VerticalAlignment = VerticalAlignment.Bottom;

            Grid.SetRow(_window.ResultsContainerWrapper, 0);
            Grid.SetRow(_window.ResultsSeparator, 1);
            Grid.SetRow(_window.SearchBoxBorder, 2);
            Grid.SetRow(_window.PathPreviewBorder, 0);
            Grid.SetRow(_window.ResultsPanelControl, 1);
            _window.PathPreviewBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            _window.PathPreviewBorder.CornerRadius = new CornerRadius(7, 7, 0, 0);
            _window.MainBorder.CornerRadius = new CornerRadius(8);
            _window.SearchBoxBorder.CornerRadius = isResultsVisible ? new CornerRadius(0, 0, 7, 7) : new CornerRadius(7);
        }

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
            if (hasValidRect)
            {
                var winLeft = rect.Left * dpiScaleX;
                var winTop = rect.Top * dpiScaleY;
                var winRight = rect.Right * dpiScaleX;
                var winBottom = rect.Bottom * dpiScaleY;

                double targetLeft = 0;
                double targetTop = 0;

                if (useDialogMode)
                {
                    var winWidth = (rect.Right - rect.Left) * dpiScaleX;
                    targetLeft = winLeft + (winWidth - windowWidth) / 2;
                    // Align top of search window to bottom of dialog
                    targetTop = winBottom - xamlMargin + visibleMargin;
                }
                else if (tracker.IsActiveWindowDialog)
                {
                    // Standard (upward) mode: searchbox (Row 2 = bottom of card) should align to dialog bottom.
                    // card.Bottom = window.Top + windowHeight, and we want card.Bottom ≈ winBottom,
                    // but the card's bottom Row is the searchbox. Offset by one searchbox height to keep it visible.
                    var winWidth = (rect.Right - rect.Left) * dpiScaleX;
                    targetLeft = winLeft + (winWidth - windowWidth) / 2;
                    var searchBoxHeight = _window.SearchBoxBorder.ActualHeight > 0 ? _window.SearchBoxBorder.ActualHeight : 48.0;
                    targetTop = winBottom - windowHeight + xamlMargin + searchBoxHeight;
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
                if (targetLeft > maxLeft) targetLeft = maxLeft;

                if (useDialogMode)
                {
                    // In dialog mode, keep the visible card bottom within the screen
                    var maxDialogModeTop = workingArea.Bottom * dpiScaleY - visibleHeight;
                    if (targetTop > maxDialogModeTop) targetTop = maxDialogModeTop;
                    if (targetTop < minTop) targetTop = minTop;
                }
                else if (tracker.IsActiveWindowDialog)
                {
                    // In standard mode on dialog, keep the visible card top within the screen
                    var minDialogModeTop = workingArea.Top * dpiScaleY - windowHeight + visibleHeight;
                    if (targetTop < minDialogModeTop) targetTop = minDialogModeTop;
                    if (targetTop > maxTop) targetTop = maxTop;
                }
                else
                {
                    if (targetTop < minTop) targetTop = minTop;
                    if (targetTop > maxTop) targetTop = maxTop;
                }

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
