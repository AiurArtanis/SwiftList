using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftList.App.Services;

using SwiftList.App.Services.Theme;
using SwiftList.App.Helpers.Visuals;
namespace SwiftList.App.Views.Controls.Dialogs;

public partial class CustomMessageBoxWindow : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public CustomMessageBoxWindow(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        InitializeComponent();

        SystemMenuBlocker.Attach(this);
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
                TxtIcon.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
                TxtIcon.Visibility = Visibility.Visible;
                break;
            case MessageBoxImage.Warning: // Exclamation
                TxtIcon.Text = "\uE7BA";
                TxtIcon.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
                TxtIcon.Visibility = Visibility.Visible;
                break;
            case MessageBoxImage.Information: // Asterisk
                TxtIcon.Text = "\uE946";
                TxtIcon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBlue");
                TxtIcon.Visibility = Visibility.Visible;
                break;
            case MessageBoxImage.Question:
                TxtIcon.Text = "\uE9CE";
                TxtIcon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBlue");
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

    private void BtnOK_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.OK;
        Close();
    }
}
