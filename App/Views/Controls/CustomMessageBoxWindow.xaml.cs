using System.Windows;
using System.Windows.Input;
using SwiftList.App.Services;

namespace SwiftList.App.Views.Controls;

public partial class CustomMessageBoxWindow : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public CustomMessageBoxWindow(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        InitializeComponent();

        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);

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
                TxtIcon.Foreground = ThemedBrush("ErrorBrush", 239, 68, 68);
                TxtIcon.Visibility = Visibility.Visible;
                break;
            case MessageBoxImage.Warning: // Exclamation
                TxtIcon.Text = "\uE7BA";
                TxtIcon.Foreground = ThemedBrush("WarningBrush", 245, 158, 11);
                TxtIcon.Visibility = Visibility.Visible;
                break;
            case MessageBoxImage.Information: // Asterisk
                TxtIcon.Text = "\uE946";
                TxtIcon.Foreground = ThemedBrush("AccentBlue", 59, 130, 246);
                TxtIcon.Visibility = Visibility.Visible;
                break;
            case MessageBoxImage.Question:
                TxtIcon.Text = "\uE9CE";
                TxtIcon.Foreground = ThemedBrush("AccentBlue", 59, 130, 246);
                TxtIcon.Visibility = Visibility.Visible;
                break;
            default:
                TxtIcon.Visibility = Visibility.Collapsed;
                break;
        }
    }

    // Looks up the current theme's brush by key, falling back to the fixed color if the theme
    // doesn't define it, so the icon color follows the active theme instead of a baked-in hex value.
    private static System.Windows.Media.Brush ThemedBrush(string resourceKey, byte r, byte g, byte b)
        => System.Windows.Application.Current?.TryFindResource(resourceKey) as System.Windows.Media.Brush
           ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BtnOK_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.OK;
        Close();
    }
}
