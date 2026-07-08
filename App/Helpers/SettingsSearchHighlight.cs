using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;

namespace SwiftList.App.Helpers;

// Draws a brief accent-colored outline around a settings control after search navigates to it,
// mirroring Windows 11 Settings' "flash the matched control" behavior. Uses an Adorner rather than
// wrapping every settings row in a Border, since the target can be any control type (CheckBox, Grid,
// Button...) and this way none of the existing settings XAML needs restructuring -- SettingsWindow.xaml
// only needs an AdornerDecorator somewhere above the settings pages for GetAdornerLayer to find.
public static class SettingsSearchHighlight
{
    public static void Show(FrameworkElement target)
    {
        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer == null)
            return;

        var brush = target.TryFindResource("AccentBlue") as Brush ?? System.Windows.Media.Brushes.DodgerBlue;
        var adorner = new FlashAdorner(target, brush);
        layer.Add(adorner);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(900))
        {
            BeginTime = TimeSpan.FromMilliseconds(700),
        };
        fade.Completed += (_, _) => layer.Remove(adorner);
        adorner.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private sealed class FlashAdorner : Adorner
    {
        private readonly Pen _pen;

        public FlashAdorner(UIElement adornedElement, Brush brush) : base(adornedElement)
        {
            _pen = new Pen(brush, 2);
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var rect = new Rect(AdornedElement.RenderSize);
            rect.Inflate(5, 5);
            drawingContext.DrawRoundedRectangle(null, _pen, rect, 6, 6);
        }
    }
}
