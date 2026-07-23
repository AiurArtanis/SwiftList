using SwiftList.PluginSdk;

namespace SwiftList.Plugins.FolderCascader.Navigation;

// Renders and caches the small set of GDI HBITMAPs the cascader menu draws (folder/file/favorites/
// history icons). Kept separate from history lookups and Explorer-window enumeration -- icon rendering
// changes for theming reasons, not for either of those.
public static class IconBitmapCache
{
    public static IntPtr FolderHBitmap { get; private set; } = IntPtr.Zero;
    public static IntPtr FileHBitmap { get; private set; } = IntPtr.Zero;
    public static IntPtr FavoritesHBitmap { get; private set; } = IntPtr.Zero;
    public static IntPtr HistoryHBitmap { get; private set; } = IntPtr.Zero;

    private static readonly object _iconLock = new();

    [System.Runtime.InteropServices.DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    public static void EnsureIcons()
    {
        if (FolderHBitmap == IntPtr.Zero)
        {
            lock (_iconLock)
            {
                if (FolderHBitmap == IntPtr.Zero)
                {
                    try
                    {
                        FolderHBitmap = ShellIconLoader.GetIconHBitmap("dummy_folder", isDir: true);
                        FileHBitmap = ShellIconLoader.GetIconHBitmap("dummy_file.txt", isDir: false);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[FolderCascader] Failed to create cached shell HBITMAP icons: {ex.Message}", LogLevel.Warn);
                    }
                }
            }
        }

        lock (_iconLock)
        {
            try
            {
                if (FavoritesHBitmap != IntPtr.Zero)
                {
                    DeleteObject(FavoritesHBitmap);
                }
                if (HistoryHBitmap != IntPtr.Zero)
                {
                    DeleteObject(HistoryHBitmap);
                }

                FavoritesHBitmap = CreateStarHBitmap();
                HistoryHBitmap = CreateClockHBitmap();
            }
            catch (Exception ex)
            {
                Logger.Log($"[FolderCascader] Failed to update themed icons: {ex.Message}", LogLevel.Warn);
            }
        }
    }

    private static IntPtr CreateHBitmapFromWpfPath(string pathData, System.Windows.Media.Brush? fill, System.Windows.Media.Pen? stroke)
    {
        var geometry = System.Windows.Media.Geometry.Parse(pathData);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new System.Windows.Media.ScaleTransform(4.0, 4.0));
            dc.DrawGeometry(fill, stroke, geometry);
            dc.Pop();
        }

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(64, 64, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(visual);

        var stride = 64 * 4;
        var pixels = new byte[64 * stride];
        rtb.CopyPixels(pixels, stride, 0);

        using var bmp = new System.Drawing.Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var rect = new System.Drawing.Rectangle(0, 0, 64, 64);
        var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
        bmp.UnlockBits(bmpData);

        return bmp.GetHbitmap();
    }

    private static IntPtr CreateStarHBitmap()
    {
        var path = "M 8,1.5 L 10.2,6 L 15,6.5 L 11.3,9.7 L 12.5,14.5 L 8,12 L 3.5,14.5 L 4.7,9.7 L 1,6.5 L 5.8,6 Z";
        var warningBrush = System.Windows.Application.Current?.TryFindResource("WarningBrush") as System.Windows.Media.SolidColorBrush;
        var fill = warningBrush ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7));
        var stroke = new System.Windows.Media.Pen(fill, 1.0);
        return CreateHBitmapFromWpfPath(path, fill, stroke);
    }

    private static IntPtr CreateClockHBitmap()
    {
        var path = "M 8,2 A 6,6 0 1,0 8.001,2 M 8,5 L 8,8 L 11,8";
        var accentBrush = System.Windows.Application.Current?.TryFindResource("AccentBlue") as System.Windows.Media.SolidColorBrush;
        var strokeBrush = accentBrush ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
        var stroke = new System.Windows.Media.Pen(strokeBrush, 1.5);
        return CreateHBitmapFromWpfPath(path, null, stroke);
    }
}
