using System.Windows;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SwiftList.App.ViewModels.Search;
using SwiftList.Core;
using Application = System.Windows.Application;

namespace SwiftList.App.Services;

public class TrayIconService : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private NotifyIcon? _notifyIcon;
    private readonly QuickSearchViewModel _viewModel;
    private readonly Action _showWindowAction;
    private readonly Action _toggleVisibilityAction;
    private IntPtr _hIcon = IntPtr.Zero;

    private System.Windows.Controls.ContextMenu? _wpfContextMenu;
    private System.Windows.Controls.MenuItem? _wpfItemShowWindow;
    private System.Windows.Controls.MenuItem? _wpfItemToggleHotkeys;
    private System.Windows.Controls.MenuItem? _wpfItemSettings;
    private System.Windows.Controls.MenuItem? _wpfItemAbout;
    private System.Windows.Controls.MenuItem? _wpfItemCleanExit;
    private System.Windows.Controls.MenuItem? _wpfItemExit;
    private Window? _dummyWindow;
    private bool _isHotkeysDisabled;

    public TrayIconService(QuickSearchViewModel viewModel, Action showWindowAction, Action toggleVisibilityAction)
    {
        _viewModel = viewModel;
        _showWindowAction = showWindowAction;
        _toggleVisibilityAction = toggleVisibilityAction;
        InitializeNotifyIcon();

        ThemeManager.Instance.ThemeChanged += UpdateTrayIconThemeColor;
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        UpdateMenuTexts();
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateMenuTexts();

    private void InitializeNotifyIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "SwiftList",
            Visible = true
        };

        UpdateTrayIconThemeColor();

        _notifyIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _toggleVisibilityAction();
            }
            else if (e.Button == MouseButtons.Right)
            {
                ShowWpfContextMenu();
            }
        };
    }

    private void UpdateTrayIconThemeColor()
    {
        if (_notifyIcon == null) return;
        try
        {
            Color drawingColor;
            if (ThemeManager.Instance.ActiveTheme?.IsDark == true)
            {
                drawingColor = Color.White;
            }
            else
            {
                var brush = Application.Current.Resources["AccentBlue"] as System.Windows.Media.SolidColorBrush;
                var mediaColor = brush?.Color ?? System.Windows.Media.Colors.DodgerBlue;
                drawingColor = Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
            }

            var resourceUri = new Uri("pack://application:,,,/SwiftList.App;component/logo.png", UriKind.Absolute);
            var resourceInfo = Application.GetResourceStream(resourceUri);
            if (resourceInfo == null) return;

            using var originalStream = resourceInfo.Stream;
            using var originalBitmap = new Bitmap(originalStream);

            // Get target dimensions based on current DPI scaling
            var iconWidth = SystemInformation.SmallIconSize.Width;
            var iconHeight = SystemInformation.SmallIconSize.Height;

            using var coloredBitmap = new Bitmap(iconWidth, iconHeight);
            using (var g = Graphics.FromImage(coloredBitmap))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                using var attributes = new ImageAttributes();
                var colorMatrix = new ColorMatrix(new float[][]
                {
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, drawingColor.A / 255f, 0 },
                    new float[] { drawingColor.R / 255f, drawingColor.G / 255f, drawingColor.B / 255f, 0, 1 }
                });
                attributes.SetColorMatrix(colorMatrix);
                g.DrawImage(originalBitmap,
                    new Rectangle(0, 0, iconWidth, iconHeight),
                    0, 0, originalBitmap.Width, originalBitmap.Height,
                    GraphicsUnit.Pixel, attributes);
            }

            var oldHIcon = _hIcon;
            _hIcon = coloredBitmap.GetHicon();
            _notifyIcon.Icon = Icon.FromHandle(_hIcon);

            if (oldHIcon != IntPtr.Zero)
            {
                DestroyIcon(oldHIcon);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[TrayIconService] Failed to update tray icon theme color: {ex.Message}", LogLevel.Error);
        }
    }

    private void InitializeWpfContextMenu()
    {
        _wpfContextMenu = new System.Windows.Controls.ContextMenu();

        _wpfItemShowWindow = new System.Windows.Controls.MenuItem
        {
            Icon = CreateIcon("\uE721", "AccentBlue")
        };
        _wpfItemShowWindow.Click += (s, e) => ShowSearchWindow();

        _wpfItemToggleHotkeys = new System.Windows.Controls.MenuItem();
        _wpfItemToggleHotkeys.Click += (s, e) => ToggleHotkeys();

        _wpfItemSettings = new System.Windows.Controls.MenuItem
        {
            Icon = CreateIcon("\uE713", "MenuText")
        };
        _wpfItemSettings.Click += (s, e) => ShowSettingsWindow();

        _wpfItemAbout = new System.Windows.Controls.MenuItem
        {
            Icon = CreateIcon("\uE946", "AccentBlue")
        };
        _wpfItemAbout.Click += (s, e) => ShowSettingsWindow("About");

        _wpfItemCleanExit = new System.Windows.Controls.MenuItem
        {
            Icon = CreateIcon("\uE74D", "AccentBlue")
        };
        _wpfItemCleanExit.Click += (s, e) => TrayCleanExitHelper.CleanExit();

        _wpfItemExit = new System.Windows.Controls.MenuItem
        {
            Icon = CreateIcon("\uF3B1", "MenuText")
        };
        _wpfItemExit.Click += (s, e) => Application.Current.Shutdown();

        _wpfContextMenu.Items.Add(_wpfItemShowWindow);
        _wpfContextMenu.Items.Add(_wpfItemToggleHotkeys);
        _wpfContextMenu.Items.Add(_wpfItemSettings);
        _wpfContextMenu.Items.Add(new System.Windows.Controls.Separator());
        _wpfContextMenu.Items.Add(_wpfItemAbout);
        _wpfContextMenu.Items.Add(new System.Windows.Controls.Separator());
        _wpfContextMenu.Items.Add(_wpfItemCleanExit);
        _wpfContextMenu.Items.Add(_wpfItemExit);

        UpdateMenuTexts();
    }

    private static UIElement CreateIcon(string glyph, string resourceKey)
    {
        var tb = new System.Windows.Controls.TextBlock
        {
            Text = glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        tb.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, resourceKey);
        return tb;
    }

    private void ShowWpfContextMenu()
    {
        if (_wpfContextMenu == null)
        {
            InitializeWpfContextMenu();
        }

        UpdateCleanExitVisibility();

        if (_dummyWindow != null)
        {
            try { _dummyWindow.Close(); } catch { }
            _dummyWindow = null;
        }

        _dummyWindow = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true
        };

        _dummyWindow.Show();
        _dummyWindow.Activate();

        _wpfContextMenu!.PlacementTarget = _dummyWindow;
        _wpfContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;

        RoutedEventHandler? closedHandler = null;
        closedHandler = (s, e) =>
        {
            _wpfContextMenu.Closed -= closedHandler;
            try
            {
                _dummyWindow?.Close();
            }
            catch { }
            _dummyWindow = null;
        };
        _wpfContextMenu.Closed += closedHandler;

        _wpfContextMenu.IsOpen = true;
    }

    private void UpdateMenuTexts()
    {
        _wpfItemShowWindow?.Header = TranslationManager.Instance["Tray_ShowWindow"];
        _wpfItemSettings?.Header = TranslationManager.Instance["Tray_Settings"];
        _wpfItemAbout?.Header = TranslationManager.Instance["Tray_About"];
        _wpfItemCleanExit?.Header = TranslationManager.Instance["Tray_CleanExit"];
        _wpfItemExit?.Header = TranslationManager.Instance["Tray_Exit"];
        UpdateHotkeysMenuState();
    }

    private void ToggleHotkeys()
    {
        if (_wpfItemToggleHotkeys == null) return;
        _isHotkeysDisabled = !_isHotkeysDisabled;
        App.HookClient?.IsHotkeysDisabled = _isHotkeysDisabled;
        UpdateHotkeysMenuState();
    }

    private void UpdateHotkeysMenuState()
    {
        if (_wpfItemToggleHotkeys == null) return;
        _wpfItemToggleHotkeys.Header = TranslationManager.Instance["Tray_ToggleHotkeys"];
        var isDisabled = App.HookClient != null ? App.HookClient.IsHotkeysDisabled : _isHotkeysDisabled;
        if (isDisabled)
        {
            _wpfItemToggleHotkeys.Icon = CreateIcon("\uE73E", "AccentBlue");
        }
        else
        {
            _wpfItemToggleHotkeys.Icon = CreateIcon("\uE71A", "MenuText");
        }
    }

    private void ShowSettingsWindow(string? targetSection = null) => App.ShowSettingsWindow(targetSection);
    private void ShowSearchWindow() => App.ShowSearchWindow();

    private void UpdateCleanExitVisibility() => _wpfItemCleanExit?.Visibility = TrayCleanExitHelper.IsOnlyAppProcessRunning() ? Visibility.Visible : Visibility.Collapsed;

    public void Dispose()
    {
        ThemeManager.Instance.ThemeChanged -= UpdateTrayIconThemeColor;
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
        App.CloseAllManagedWindows();

        if (_dummyWindow != null) { try { _dummyWindow.Close(); } catch { } _dummyWindow = null; }
        if (_notifyIcon != null) { _notifyIcon.Visible = false; _notifyIcon.Dispose(); _notifyIcon = null; }
        if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
    }
}
