using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SwiftList.App;

internal static class ShellIconShortcutResolver
{
    public static ImageSource? TryGetShortcutTargetIcon(string shortcutPath)
    {
        object? shellLinkObject = null;
        try
        {
            shellLinkObject = new ShellIconNativeMethods.ShellLink();
            var shellLink = (ShellIconNativeMethods.IShellLinkW)shellLinkObject;
            var persistFile = (IPersistFile)shellLinkObject;
            persistFile.Load(shortcutPath, 0);

            var iconPathBuilder = new StringBuilder(ShellIconNativeMethods.MAX_PATH);
            shellLink.GetIconLocation(iconPathBuilder, iconPathBuilder.Capacity, out var iconIndex);
            var iconPath = Environment.ExpandEnvironmentVariables(iconPathBuilder.ToString());

            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                var icon = ExtractSmallIcon(iconPath, iconIndex);
                if (icon != null)
                {
                    return icon;
                }
            }

            var targetPathBuilder = new StringBuilder(ShellIconNativeMethods.MAX_PATH);
            shellLink.GetPath(targetPathBuilder, targetPathBuilder.Capacity, IntPtr.Zero, ShellIconNativeMethods.SLGP_UNCPRIORITY);
            var targetPath = Environment.ExpandEnvironmentVariables(targetPathBuilder.ToString());
            if (!string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath))
            {
                return ExtractSmallIcon(targetPath, 0) ?? GetShellIconWithoutLinkOverlay(targetPath);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[ShellIconHelper] Failed to resolve shortcut icon for {shortcutPath}: {ex.Message}", Core.LogLevel.Warn);
        }
        finally
        {
            if (shellLinkObject != null)
            {
                Marshal.FinalReleaseComObject(shellLinkObject);
            }
        }

        return null;
    }

    private static ImageSource? ExtractSmallIcon(string iconPath, int iconIndex)
    {
        var smallIcons = new IntPtr[1];
        var extracted = ShellIconNativeMethods.ExtractIconEx(iconPath, iconIndex, null, smallIcons, 1);
        if (extracted == 0 || smallIcons[0] == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                smallIcons[0],
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            ShellIconNativeMethods.DestroyIcon(smallIcons[0]);
        }
    }

    public static ImageSource? GetShellIconWithoutLinkOverlay(string targetPath)
    {
        var shfi = new ShellIconNativeMethods.SHFILEINFOW();
        var res = ShellIconNativeMethods.SHGetFileInfoW(targetPath, 0, ref shfi, (uint)Marshal.SizeOf(shfi), ShellIconNativeMethods.SHGFI_ICON | ShellIconNativeMethods.SHGFI_SMALLICON);
        if (res == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                shfi.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            ShellIconNativeMethods.DestroyIcon(shfi.hIcon);
        }
    }
}
