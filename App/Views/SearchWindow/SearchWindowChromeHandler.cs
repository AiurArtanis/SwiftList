using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TextBox = System.Windows.Controls.TextBox;

namespace SwiftList.App.Views.SearchWindow
{
    public class SearchWindowChromeHandler
    {
        private readonly SwiftList.App.SearchWindow _window;

        public SearchWindowChromeHandler(SwiftList.App.SearchWindow window)
        {
            _window = window;
        }

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
            if (_window.BtnMaximize != null)
            {
                _window.BtnMaximize.Content = _window.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            }

            if (_window.DragGrip != null)
            {
                _window.DragGrip.Visibility = _window.WindowState == WindowState.Maximized ? Visibility.Collapsed : Visibility.Visible;
            }

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

        public void Minimize()
        {
            _window.WindowState = WindowState.Minimized;
        }

        public void ToggleMaximize()
        {
            _window.WindowState = _window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        public void Close()
        {
            _window.Close();
        }
    }
}
