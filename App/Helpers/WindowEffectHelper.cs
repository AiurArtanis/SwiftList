using System.Windows;

namespace SwiftList.App.Helpers;

public static class WindowEffectHelper
{
    public static void ApplyThemeEffects(Window window, PluginSdk.ITheme theme)
    {
        var opacity = theme.WindowOpacity;

        // Apply Window Opacity (WPF side) only if AllowsTransparency is enabled on the window
        if (window.AllowsTransparency && window.Content is FrameworkElement rootElement)
        {
            rootElement.Opacity = opacity;
        }
    }
}
