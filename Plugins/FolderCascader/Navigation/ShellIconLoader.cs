using System.Runtime.InteropServices;

namespace SwiftList.Plugins.FolderCascader.Navigation;

internal static class ShellIconLoader
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFOW pszFileInfo, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    public static IntPtr GetIconHBitmap(string path, bool isDir)
    {
        var shfi = new SHFILEINFOW();
        var flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
        var attributes = isDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

        var res = SHGetFileInfoW(path, attributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
        if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
        {
            try
            {
                if (GetIconInfo(shfi.hIcon, out var iconInfo))
                {
                    if (iconInfo.hbmMask != IntPtr.Zero)
                        DeleteObject(iconInfo.hbmMask);
                    return iconInfo.hbmColor;
                }
            }
            finally
            {
                DestroyIcon(shfi.hIcon);
            }
        }
        return IntPtr.Zero;
    }
}
