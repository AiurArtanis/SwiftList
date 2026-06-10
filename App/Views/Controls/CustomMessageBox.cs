using System;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace SwiftList.App.Views.Controls
{
    public static class CustomMessageBox
    {
        public static MessageBoxResult Show(string messageBoxText)
        {
            return Show(null, messageBoxText, "SwiftList", MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption)
        {
            return Show(null, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
        {
            return Show(null, messageBoxText, caption, button, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            return Show(null, messageBoxText, caption, button, icon);
        }

        public static MessageBoxResult Show(Window owner, string messageBoxText)
        {
            return Show(owner, messageBoxText, "SwiftList", MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption)
        {
            return Show(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button)
        {
            return Show(owner, messageBoxText, caption, button, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            if (Application.Current == null)
            {
                // Fallback to standard system MessageBox if WPF application is not active
                return MessageBox.Show(messageBoxText, caption, button, icon);
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                return ShowInternal(owner, messageBoxText, caption, button, icon);
            }
            else
            {
                return Application.Current.Dispatcher.Invoke(() => ShowInternal(owner, messageBoxText, caption, button, icon));
            }
        }

        private static MessageBoxResult ShowInternal(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            var win = new CustomMessageBoxWindow(messageBoxText, caption, button, icon);

            // Set Owner to the specified window or attempt to find the active window
            if (owner != null)
            {
                win.Owner = owner;
            }
            else if (Application.Current != null)
            {
                // Find active window or main window to act as owner
                foreach (Window w in Application.Current.Windows)
                {
                    if (w.IsActive && w.IsVisible && w != win)
                    {
                        win.Owner = w;
                        break;
                    }
                }

                if (win.Owner == null && Application.Current.MainWindow != null && Application.Current.MainWindow != win && Application.Current.MainWindow.IsVisible)
                {
                    win.Owner = Application.Current.MainWindow;
                }
            }

            win.ShowDialog();
            return win.Result;
        }
    }
}
