using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk;
using SwiftList.PluginSdk.Services;
using System.Threading;

namespace SwiftList.Plugins.CoreExtensions.Providers;

public class ShellThumbnailProvider : IThumbnailProvider
{
    public string Id => "CoreExtensions::ThumbnailProvider::Shell";
    public string Name => TranslationService.Get("Plugins_SystemThumbnailProviderName");

    private static HashSet<string>? _supportedExtensions;
    private static DateTime _lastLoaded = DateTime.MinValue;
    private static readonly object ExtLock = new();

    private static HashSet<string> GetSupportedExtensions()
    {
        var now = DateTime.UtcNow;
        if (_supportedExtensions != null && (now - _lastLoaded).TotalSeconds < 3)
            return _supportedExtensions;

        lock (ExtLock)
        {
            if (_supportedExtensions != null && (now - _lastLoaded).TotalSeconds < 3)
                return _supportedExtensions;

            var defaultList = new List<string>
            {
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif",
                ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".m4v"
            };

            try
            {
                var list = PluginSettingsService.GetSetting<List<string>>(
                    "SwiftList.Plugins.CoreExtensions", 
                    "ThumbnailExtensions", 
                    defaultList);
                if (list != null)
                {
                    _supportedExtensions = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                _supportedExtensions = null;
            }

            _supportedExtensions ??= new HashSet<string>(defaultList, StringComparer.OrdinalIgnoreCase);
            _lastLoaded = now;
        }
        return _supportedExtensions;
    }

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject([In] IntPtr hObject);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        [In] IntPtr pbc,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(
            [In] long size,
            [In] int flags,
            [Out] out IntPtr phbm);
    }

    public bool CanProvideThumbnail(string path, bool isDir)
    {
        if (isDir || string.IsNullOrWhiteSpace(path))
            return false;

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;

        return GetSupportedExtensions().Contains(ext);
    }

    public ImageSource? GetThumbnail(string path, int size)
    {
        IntPtr hBitmap = IntPtr.Zero;
        IShellItemImageFactory? factory = null;
        try
        {
            if (!File.Exists(path))
                return null;

            var uuid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"); // IID_IShellItemImageFactory
            var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, uuid, out factory);

            if (hr == 0 && factory != null)
            {
                // Pack cx and cy into a 64-bit long: lower 32 bits = cx, upper 32 bits = cy
                long packedSize = ((long)size) | (((long)size) << 32);

                // SIIGBF_RESIZETOFIT = 0x0
                var hrGetImage = factory.GetImage(packedSize, 0x0, out hBitmap);
                if (hrGetImage == 0 && hBitmap != IntPtr.Zero)
                {
                    var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    // Freeze to make it thread-safe and cross-thread usable in WPF
                    bitmapSource.Freeze();
                    return bitmapSource;
                }
            }
        }
        catch
        {
            // Fail silently and fallback to standard icon
        }
        finally
        {
            if (factory != null)
            {
                Marshal.ReleaseComObject(factory);
            }
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }
        }

        return null;
    }
}
