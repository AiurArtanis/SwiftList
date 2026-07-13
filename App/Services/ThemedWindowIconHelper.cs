using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;

namespace SwiftList.App.Services;

/// <summary>
/// Sets a window's title-bar icon (native Window.Icon, and/or an in-window logo Image) to a
/// monochrome, theme-colored render of tray.png -- the same silhouette source TrayIconService
/// recolors for the system tray icon, just recolored here as a plain WPF BitmapSource (both
/// Window.Icon and Image.Source accept any ImageSource, so no .ico conversion is needed).
/// Re-renders whenever the active theme changes.
/// </summary>
public static class ThemedWindowIconHelper
{
    private static readonly Uri SourceUri = new("pack://application:,,,/SwiftList.App;component/tray.png", UriKind.Absolute);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW", SetLastError = true)]
    private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    // WPF's Window.Icon property only updates the per-window icon that Alt-Tab/the DWM thumbnail
    // switcher reads directly -- for these AllowsTransparency/WindowStyle=None windows the taskbar
    // band's own button icon instead falls back to the window CLASS icon (GCLP_HICON/GCLP_HICONSM),
    // which Window.Icon never touches, so the taskbar kept showing the exe's static shortcut icon
    // even while the window (with its correctly-themed title bar and Alt-Tab icon) was open. Setting
    // the class icon explicitly here -- via the same ThemedIconRenderer.CreateThemedHIcon pipeline
    // TrayIconService uses for the tray icon -- is what makes the running app's taskbar button
    // actually follow the theme too, instead of falling back to the exe's static resource icon.
    public static void Apply(Window window)
    {
        var currentSmallHIcon = IntPtr.Zero;
        var currentBigHIcon = IntPtr.Zero;

        void Update()
        {
            window.Icon = Render();
            ApplyNativeIcon(window, ref currentSmallHIcon, ref currentBigHIcon);
        }

        Update();

        void OnSourceInitialized(object? s, EventArgs e) => Update();
        window.SourceInitialized += OnSourceInitialized;

        void OnThemeChanged() => window.Dispatcher.Invoke(Update);
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;

        window.Closed += (_, _) =>
        {
            ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
            window.SourceInitialized -= OnSourceInitialized;
            if (currentSmallHIcon != IntPtr.Zero)
                DestroyIcon(currentSmallHIcon);
            if (currentBigHIcon != IntPtr.Zero)
                DestroyIcon(currentBigHIcon);
        };
    }

    public static void Apply(Image image, Window window) => ApplyCore(bmp => image.Source = bmp, window);

    private static void ApplyCore(Action<BitmapSource> setIcon, Window window)
    {
        void Update() => setIcon(Render());
        Update();

        void OnThemeChanged() => window.Dispatcher.Invoke(Update);
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;
        window.Closed += (_, _) => ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
    }

    private static void ApplyNativeIcon(Window window, ref IntPtr currentSmallHIcon, ref IntPtr currentBigHIcon)
    {
        // No-op until the HWND actually exists (the very first call happens right after
        // InitializeComponent, well before Show()) -- the SourceInitialized hook above re-runs
        // Update() once it does, so this isn't skipped permanently.
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        IntPtr newSmall, newBig;
        try
        {
            newSmall = ThemedIconRenderer.CreateThemedHIcon(System.Windows.Forms.SystemInformation.SmallIconSize);
            newBig = ThemedIconRenderer.CreateThemedHIcon(System.Windows.Forms.SystemInformation.IconSize);
        }
        catch
        {
            return;
        }

        SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, newSmall);
        SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, newBig);
        SetClassLongPtr(hwnd, GCLP_HICONSM, newSmall);
        SetClassLongPtr(hwnd, GCLP_HICON, newBig);

        var oldSmall = currentSmallHIcon;
        var oldBig = currentBigHIcon;
        currentSmallHIcon = newSmall;
        currentBigHIcon = newBig;
        if (oldSmall != IntPtr.Zero)
            DestroyIcon(oldSmall);
        if (oldBig != IntPtr.Zero)
            DestroyIcon(oldBig);
    }

    private static BitmapSource Render()
    {
        var c = ThemedIconRenderer.GetThemeColor();
        var color = Color.FromArgb(c.A, c.R, c.G, c.B);

        var source = new BitmapImage(SourceUri);
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        // Bgra32 is straight (non-premultiplied) alpha, stored B,G,R,A per pixel -- replace the color
        // channels with the theme color while keeping the source alpha, i.e. its silhouette shape.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
        }

        var bitmap = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }
}
