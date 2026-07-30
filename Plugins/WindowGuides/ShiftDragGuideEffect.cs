using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using SwiftList.PluginSdk.Abstractions.Plugins.WindowEffects;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.WindowGuides;

public sealed class ShiftDragGuideEffect : IQuickSearchWindowDragEffectProvider
{
    private const string PluginId = "SwiftList.Plugins.WindowGuides";
    private const int MonitorDefaultToNearest = 2;
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExNoActivate = 0x08000000;
    private Window? _overlay;
    private Canvas? _canvas;
    private Line? _verticalGuide;
    private Line? _horizontalGuide;
    private Rectangle? _windowOutline;

    public string Name => "Shift Drag Guides";
    public string Description => "Shows and snaps to the active screen's center guides while Shift is held during a quick-search drag.";

    public void OnDragStarted(Window window) { }

    public void OnDragMoved(Window window, FrameworkElement searchCard)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            HideOverlay();
            return;
        }

        var screenBounds = GetMonitorBounds(window);
        var windowBounds = GetWindowBounds(window);
        var center = new Point(screenBounds.Left + screenBounds.Width / 2, screenBounds.Top + screenBounds.Height / 2);
        var snapped = GuideSnapCalculator.Snap(windowBounds, center);
        MoveWindowByPixels(window, snapped.Left - windowBounds.Left, snapped.Top - windowBounds.Top);
        Draw(window, searchCard, screenBounds, center);
    }

    public void OnDragEnded(Window window) => HideOverlay();

    private void Draw(Window owner, FrameworkElement searchCard, Rect screenBounds, Point center)
    {
        EnsureOverlay(owner);
        if (_overlay == null || _canvas == null || _verticalGuide == null || _horizontalGuide == null || _windowOutline == null)
            return;

        if (!_overlay.IsVisible) _overlay.Show();
        SetWindowPos(new WindowInteropHelper(_overlay).Handle, IntPtr.Zero,
            (int)screenBounds.Left, (int)screenBounds.Top, (int)screenBounds.Width, (int)screenBounds.Height,
            0x0010 | 0x0004);
        _overlay.UpdateLayout();

        var overlayTopLeft = _overlay.PointFromScreen(new Point(screenBounds.Left, screenBounds.Top));
        var overlayBottomRight = _overlay.PointFromScreen(new Point(screenBounds.Right, screenBounds.Bottom));
        var overlayCenter = _overlay.PointFromScreen(center);
        var cardTopLeft = _overlay.PointFromScreen(searchCard.PointToScreen(new Point(0, 0)));
        var cardBottomRight = _overlay.PointFromScreen(searchCard.PointToScreen(new Point(searchCard.ActualWidth, searchCard.ActualHeight)));
        var accent = WithOpacity(FindBrush("AccentColor", "AccentBlue").Color, GetSetting("OutlineOpacity", 50));
        var baseColor = WithOpacity(FindBrush("CardBorderBrush", "BorderColor").Color, GetSetting("GuideOpacity", 50));

        _canvas.Width = overlayBottomRight.X - overlayTopLeft.X;
        _canvas.Height = overlayBottomRight.Y - overlayTopLeft.Y;
        _verticalGuide.Stroke = new SolidColorBrush(baseColor);
        _verticalGuide.StrokeThickness = GetThickness("GuideThickness", 1);
        _verticalGuide.X1 = _verticalGuide.X2 = overlayCenter.X;
        _verticalGuide.Y1 = 0;
        _verticalGuide.Y2 = _canvas.Height;
        _horizontalGuide.Stroke = new SolidColorBrush(baseColor);
        _horizontalGuide.StrokeThickness = GetThickness("GuideThickness", 1);
        _horizontalGuide.X1 = 0;
        _horizontalGuide.X2 = _canvas.Width;
        _horizontalGuide.Y1 = _horizontalGuide.Y2 = overlayCenter.Y;
        _windowOutline.Stroke = new SolidColorBrush(accent);
        _windowOutline.StrokeThickness = GetThickness("OutlineThickness", 2);
        _windowOutline.Width = cardBottomRight.X - cardTopLeft.X;
        _windowOutline.Height = cardBottomRight.Y - cardTopLeft.Y;
        Canvas.SetLeft(_windowOutline, cardTopLeft.X);
        Canvas.SetTop(_windowOutline, cardTopLeft.Y);
    }

    private void EnsureOverlay(Window owner)
    {
        if (_overlay != null) return;
        _canvas = new Canvas { IsHitTestVisible = false };
        _verticalGuide = CreateGuideLine();
        _horizontalGuide = CreateGuideLine();
        _windowOutline = new Rectangle { StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 4 }, IsHitTestVisible = false };
        _canvas.Children.Add(_verticalGuide);
        _canvas.Children.Add(_horizontalGuide);
        _canvas.Children.Add(_windowOutline);
        _overlay = new Window
        {
            Owner = owner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Content = _canvas,
        };
        _overlay.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(_overlay).Handle;
            SetWindowLongPtr(hwnd, GwlExStyle, GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() | WsExTransparent | WsExNoActivate);
        };
    }

    private void HideOverlay()
    {
        if (_overlay?.IsVisible == true) _overlay.Hide();
    }

    private static Line CreateGuideLine() => new() { StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 }, IsHitTestVisible = false };

    private static SolidColorBrush FindBrush(string preferredKey, string fallbackKey) =>
        Application.Current.TryFindResource(preferredKey) as SolidColorBrush ??
        Application.Current.TryFindResource(fallbackKey) as SolidColorBrush ??
        Brushes.Gray;

    private static int GetSetting(string key, int defaultValue) => Math.Clamp(PluginSettingsService.GetSetting(PluginId, key, defaultValue), 0, 100);

    private static int GetThickness(string key, int defaultValue) => Math.Clamp(PluginSettingsService.GetSetting(PluginId, key, defaultValue), 1, 8);

    private static Color WithOpacity(Color color, int opacityPercent) => Color.FromArgb((byte)Math.Round(opacityPercent * 255 / 100d), color.R, color.G, color.B);

    private static Rect GetMonitorBounds(Window window)
    {
        var monitor = MonitorFromWindow(new WindowInteropHelper(window).Handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref info);
        return new Rect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top);
    }

    private static Rect GetWindowBounds(Window window)
    {
        var topLeft = window.PointToScreen(new Point(0, 0));
        var bottomRight = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
        return new Rect(topLeft, bottomRight);
    }

    private static void MoveWindowByPixels(Window window, double x, double y)
    {
        if (x == 0 && y == 0) return;
        var target = PresentationSource.FromVisual(window)?.CompositionTarget;
        if (target == null) return;
        window.Left += x * target.TransformFromDevice.M11;
        window.Top += y * target.TransformFromDevice.M22;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, long value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public RectNative rcMonitor;
        public RectNative rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
