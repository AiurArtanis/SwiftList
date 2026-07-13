using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Application = System.Windows.Application;

namespace SwiftList.App.Services;

/// <summary>
/// Shared GDI+ pipeline for rendering tray.png as a theme-colored HICON at a given pixel size --
/// used by both TrayIconService (the system tray icon) and ThemedWindowIconHelper (a running
/// window's native taskbar/class icon), so the two never drift out of sync on how "themed" is
/// decided or rendered.
/// </summary>
public static class ThemedIconRenderer
{
    private static readonly Uri SourceUri = new("pack://application:,,,/SwiftList.App;component/tray.png", UriKind.Absolute);

    public static Color GetThemeColor()
    {
        if (ThemeManager.Instance.ActiveTheme?.IsDark == true)
            return Color.White;

        var brush = Application.Current.Resources["AccentBlue"] as System.Windows.Media.SolidColorBrush;
        var mediaColor = brush?.Color ?? System.Windows.Media.Colors.DodgerBlue;
        return Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
    }

    /// <summary>
    /// Renders tray.png's silhouette at <paramref name="size"/>, recolored to the current theme
    /// color. Caller owns the returned HICON and must DestroyIcon it once no longer in use.
    /// </summary>
    public static IntPtr CreateThemedHIcon(Size size)
    {
        var color = GetThemeColor();

        var resourceInfo = Application.GetResourceStream(SourceUri) ?? throw new InvalidOperationException("tray.png resource not found");
        using var originalStream = resourceInfo.Stream;
        using var originalBitmap = new Bitmap(originalStream);

        using var coloredBitmap = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(coloredBitmap))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using var attributes = new ImageAttributes();
            var colorMatrix = new ColorMatrix(new float[][]
            {
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, color.A / 255f, 0 },
                new float[] { color.R / 255f, color.G / 255f, color.B / 255f, 0, 1 }
            });
            attributes.SetColorMatrix(colorMatrix);
            g.DrawImage(originalBitmap,
                new Rectangle(0, 0, size.Width, size.Height),
                0, 0, originalBitmap.Width, originalBitmap.Height,
                GraphicsUnit.Pixel, attributes);
        }

        return coloredBitmap.GetHicon();
    }
}
