using System.Windows;

using SwiftList.App.Services.Theme;
namespace SwiftList.App.Services.ShellMenu.ActionFlyout;

internal class MenuHelperWindow : Window
{
    public MenuHelperWindow(double x, double y)
    {
        Width = 1; Height = 1; Left = x; Top = y;
        WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false; IsTabStop = false; Focusable = true;
        ThemedWindowIconHelper.Apply(this);
    }
}
