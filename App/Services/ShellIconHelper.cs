using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SwiftList.App
{
    public static class ShellIconHelper
    {
        private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);

        public static ImageSource? GetIconFromCacheOnly(string path, bool isDir, out bool needsLoad)
        {
            needsLoad = false;
            if (path == "__NO_RESULTS__") return null;
            if (path == "__SHOW_MORE__") return GetVectorIconShowMore();

            string ext = isDir ? "::directory::" : Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
            {
                ext = "::unknown::";
            }

            // Determine if it is a unique icon type
            bool isUniqueIconType = (!isDir && (
                ext.Equals(".exe", System.StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".lnk", System.StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".ico", System.StringComparison.OrdinalIgnoreCase)
            )) || isDir;

            string cacheKey = isUniqueIconType ? path : ext;

            if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
            {
                return cachedIcon;
            }

            if (isUniqueIconType)
            {
                needsLoad = true;

                // Return generic placeholder icon instantly
                string placeholderKey = isDir ? "::directory::" : "::unknown::";
                if (_iconCache.TryGetValue(placeholderKey, out var placeholder))
                {
                    return placeholder;
                }

                // Fetch placeholder icon synchronously via USEFILEATTRIBUTES fast path
                var fetchedPlaceholder = GetIconForPath(isDir ? "dummy_folder" : "dummy_unknown", isDir);
                if (fetchedPlaceholder != null)
                {
                    _iconCache[placeholderKey] = fetchedPlaceholder;
                }
                return fetchedPlaceholder;
            }
            else
            {
                // Non-unique types can be resolved synchronously (fast path, no disk access)
                return GetIconForPath(path, isDir);
            }
        }

        public static ImageSource? GetIconForPath(this string path, bool isDir)
        {
            if (path == "__NO_RESULTS__")
                return null;

            if (path == "__SHOW_MORE__")
            {
                return GetVectorIconShowMore();
            }

            string ext = isDir ? "::directory::" : Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
            {
                ext = "::unknown::";
            }

            // EXE, LNK, ICO, etc. have unique icons per file.
            // We use FullPath as cacheKey for these to avoid caching them under a single generic ".exe" key.
            // Also treat existing directories as unique icon types to extract their customized folder icons.
            string checkPath = path;
            bool isUniqueIconType = (!isDir && (
                ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".ico", StringComparison.OrdinalIgnoreCase)
            )) || (isDir && Directory.Exists(checkPath));

            string cacheKey = isUniqueIconType ? path : ext;

            if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
            {
                return cachedIcon;
            }

            try
            {
                var shfi = new ShellIconNativeMethods.SHFILEINFOW();

                if (!isDir && ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(checkPath))
                {
                    var shortcutIcon = ShellIconShortcutResolver.TryGetShortcutTargetIcon(checkPath);
                    if (shortcutIcon != null)
                    {
                        _iconCache[cacheKey] = shortcutIcon;
                        return shortcutIcon;
                    }
                }

                if (isUniqueIconType && isDir && Directory.Exists(checkPath))
                {
                    IntPtr pidl = IntPtr.Zero;
                    uint sfgaoOut = 0;
                    int hr = ShellIconNativeMethods.SHParseDisplayName(checkPath, IntPtr.Zero, out pidl, 0, out sfgaoOut);
                    if (hr == 0 && pidl != IntPtr.Zero)
                    {
                        try
                        {
                            uint flags = ShellIconNativeMethods.SHGFI_ICON | ShellIconNativeMethods.SHGFI_SMALLICON | ShellIconNativeMethods.SHGFI_PIDL;
                            IntPtr res = ShellIconNativeMethods.SHGetFileInfoW(pidl, 0, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
                            if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                            {
                                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                                    shfi.hIcon,
                                    Int32Rect.Empty,
                                    BitmapSizeOptions.FromEmptyOptions());
                                bitmapSource.Freeze();
                                _iconCache[cacheKey] = bitmapSource;
                                return bitmapSource;
                            }
                        }
                        catch (Exception ex)
                        {
                            Core.Logger.Log($"[ShellIconHelper] Failed to get PIDL shell icon for {path}: {ex.Message}", SwiftList.Core.LogLevel.Warn);
                        }
                        finally
                        {
                            if (shfi.hIcon != IntPtr.Zero)
                            {
                                ShellIconNativeMethods.DestroyIcon(shfi.hIcon);
                            }
                            ShellIconNativeMethods.CoTaskMemFree(pidl);
                        }
                    }
                }

                if (isUniqueIconType && (File.Exists(checkPath) || Directory.Exists(checkPath)))
                {
                    // For existing EXE/LNK/ICO (or folder fallback), load the actual unique embedded icon from the file path
                    uint flags = ShellIconNativeMethods.SHGFI_ICON | ShellIconNativeMethods.SHGFI_SMALLICON;
                    IntPtr res = ShellIconNativeMethods.SHGetFileInfoW(checkPath, 0, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
                    if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                    {
                        try
                        {
                            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                                shfi.hIcon,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            bitmapSource.Freeze();
                            _iconCache[cacheKey] = bitmapSource;
                            return bitmapSource;
                        }
                        finally
                        {
                            ShellIconNativeMethods.DestroyIcon(shfi.hIcon);
                        }
                    }
                }
                else
                {
                    // Generic fallback for common extensions (highly performant, zero disk I/O)
                    uint flags = ShellIconNativeMethods.SHGFI_ICON | ShellIconNativeMethods.SHGFI_SMALLICON | ShellIconNativeMethods.SHGFI_USEFILEATTRIBUTES;
                    uint attributes = isDir ? ShellIconNativeMethods.FILE_ATTRIBUTE_DIRECTORY : ShellIconNativeMethods.FILE_ATTRIBUTE_NORMAL;
                    string lookupPath = isDir ? "dummy_folder" : ext;

                    IntPtr res = ShellIconNativeMethods.SHGetFileInfoW(lookupPath, attributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
                    if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                    {
                        try
                        {
                            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                                shfi.hIcon,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            bitmapSource.Freeze();
                            _iconCache[cacheKey] = bitmapSource;
                            return bitmapSource;
                        }
                        finally
                        {
                            ShellIconNativeMethods.DestroyIcon(shfi.hIcon);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Core.Logger.Log($"[ShellIconHelper] Failed to get shell icon for {path}: {ex.Message}", SwiftList.Core.LogLevel.Warn);
            }

            return null;
        }

        private static ImageSource? _vectorIconShowMore;
        private static ImageSource GetVectorIconShowMore()
        {
            if (_vectorIconShowMore == null)
            {
                var geometry = Geometry.Parse("M14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z");
                var group = new DrawingGroup();
                var brush = System.Windows.Application.Current?.TryFindResource("AccentBlue") as System.Windows.Media.Brush
                            ?? System.Windows.Media.Brushes.Blue;
                group.Children.Add(new GeometryDrawing(brush, null, geometry));
                var image = new DrawingImage(group);
                image.Freeze();
                _vectorIconShowMore = image;
            }
            return _vectorIconShowMore;
        }

        public static ImageSource CreateVectorIcon(string pathData, string colorHexOrKey)
        {
            var geometry = Geometry.Parse(pathData);
            var group = new DrawingGroup();

            System.Windows.Media.Brush? brush = null;
            if (!string.IsNullOrEmpty(colorHexOrKey))
            {
                brush = System.Windows.Application.Current?.TryFindResource(colorHexOrKey) as System.Windows.Media.Brush;
                if (brush == null)
                {
                    try
                    {
                        brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHexOrKey));
                    }
                    catch
                    {
                        // Fallback if not a valid hex and not found in resources
                    }
                }
            }
            if (brush == null)
            {
                brush = System.Windows.Application.Current?.TryFindResource("TextPrimary") as System.Windows.Media.Brush
                        ?? System.Windows.Media.Brushes.Gray;
            }

            group.Children.Add(new GeometryDrawing(brush, null, geometry));
            var image = new DrawingImage(group);
            try
            {
                image.Freeze();
            }
            catch { }
            return image;
        }
    }
}
