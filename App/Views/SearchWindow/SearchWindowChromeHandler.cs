using System.Windows;
using System.Windows.Input;
using TextBox = System.Windows.Controls.TextBox;

namespace SwiftList.App.Views.SearchWindow;

public class SearchWindowChromeHandler
{
    private readonly SwiftList.App.SearchWindow _window;

    public SearchWindowChromeHandler(SwiftList.App.SearchWindow window) => _window = window;

    public void HandleHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else if (_window.WindowState == WindowState.Normal)
            {
                try
                {
                    _window.DragMove();
                }
                catch (InvalidOperationException)
                {
                    // Ignore standard DragMove state exceptions
                }
            }
        }
    }

    public void HandleStateChanged()
    {
        _window.BtnMaximize?.Content = _window.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

        _window.DragGrip?.Visibility = _window.WindowState == WindowState.Maximized ? Visibility.Collapsed : Visibility.Visible;

        ApplyMaximizeSizeCap();

        if (_window.MainBorder != null && _window.ClippingBorder != null)
        {
            if (_window.WindowState == WindowState.Maximized)
            {
                _window.MainBorder.CornerRadius = new CornerRadius(0);
                _window.MainBorder.Margin = new Thickness(0);
                _window.MainBorder.BorderThickness = new Thickness(0);
                _window.ClippingBorder.CornerRadius = new CornerRadius(0);
            }
            else
            {
                _window.MainBorder.CornerRadius = new CornerRadius(10);
                _window.MainBorder.Margin = new Thickness(8);
                _window.MainBorder.BorderThickness = new Thickness(1);
                _window.ClippingBorder.CornerRadius = new CornerRadius(10);
            }
        }
    }

    // A borderless (WindowStyle=None) window maximizes over the taskbar, so cap its size to the work
    // area. The cap must come from the monitor the window is actually on -- using the primary monitor's
    // size (as before) mis-sizes a maximize on a secondary screen of a different resolution/DPI.
    private void ApplyMaximizeSizeCap()
    {
        if (_window.WindowState == WindowState.Maximized)
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            var workingArea = Screen.FromHandle(handle).WorkingArea;
            var dpiScaleX = 1.0;
            var dpiScaleY = 1.0;
            var src = PresentationSource.FromVisual(_window);
            if (src?.CompositionTarget != null)
            {
                dpiScaleX = src.CompositionTarget.TransformFromDevice.M11;
                dpiScaleY = src.CompositionTarget.TransformFromDevice.M22;
            }
            _window.MaxWidth = workingArea.Width * dpiScaleX;   // physical (system-DPI space) -> DIP
            _window.MaxHeight = workingArea.Height * dpiScaleY;
        }
        else
        {
            _window.MaxWidth = double.PositiveInfinity;
            _window.MaxHeight = double.PositiveInfinity;
        }
    }

    public void Minimize() => _window.WindowState = WindowState.Minimized;

    public void ToggleMaximize() => _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    public void Close() => _window.Close();
}
