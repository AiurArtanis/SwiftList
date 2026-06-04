using System;
using System.Windows;
using System.Windows.Input;

namespace SwiftList.App.Views.Controls
{
    public partial class CustomMessageBoxWindow : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public CustomMessageBoxWindow(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            InitializeComponent();

            TxtTitle.Text = string.IsNullOrEmpty(caption) ? "SwiftList" : caption;
            TxtMessage.Text = messageBoxText;

            SetupIcon(icon);
        }

        private void SetupIcon(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Error: // Hand, Stop
                    TxtIcon.Text = "\uEA39";
                    TxtIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // Slate Red/Red-500
                    TxtIcon.Visibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Warning: // Exclamation
                    TxtIcon.Text = "\uE7BA";
                    TxtIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11)); // Warning Amber-500
                    TxtIcon.Visibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Information: // Asterisk
                    TxtIcon.Text = "\uE946";
                    TxtIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246)); // Info Blue-500
                    TxtIcon.Visibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Question:
                    TxtIcon.Text = "\uE9CE";
                    TxtIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246)); // Info Blue-500
                    TxtIcon.Visibility = Visibility.Visible;
                    break;
                default:
                    TxtIcon.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            Close();
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            Close();
        }
    }
}
