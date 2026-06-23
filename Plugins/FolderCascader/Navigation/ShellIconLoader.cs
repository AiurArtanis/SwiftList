using System.Runtime.InteropServices;

namespace SwiftList.Plugins.FolderCascader.Navigation;

internal static class ShellIconLoader
{
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
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

    [ComImport]
    [Guid("46EB2DE8-BE82-11D1-8A3A-00C04FC36182")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hIcon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, uint crMask, ref int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFOW pszFileInfo, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727", CharSet = CharSet.Unicode)]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    private static readonly Guid IID_IImageList = new Guid("46EB2DE8-BE82-11D1-8A3A-00C04FC36182");

    public static IntPtr GetIconHBitmap(string path, bool isDir)
    {
        var shfi = new SHFILEINFOW();
        var flags = SHGFI_SYSICONINDEX | SHGFI_USEFILEATTRIBUTES;
        var attributes = isDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

        var res = SHGetFileInfoW(path, attributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
        if (res != IntPtr.Zero)
        {
            // SHIL_SMALL = 1: retrieves the system small image list, which matches GetSystemMetrics(SM_CXSMICON) DPI-scaled size
            var iid = IID_IImageList;
            if (SHGetImageList(1, ref iid, out var imageList) == 0)
            {
                var hIcon = IntPtr.Zero;
                if (imageList.GetIcon(shfi.iIcon, 1, out hIcon) == 0 && hIcon != IntPtr.Zero)
                {
                    try
                    {
                        if (GetIconInfo(hIcon, out var iconInfo))
                        {
                            if (iconInfo.hbmMask != IntPtr.Zero)
                                DeleteObject(iconInfo.hbmMask);
                            return iconInfo.hbmColor;
                        }
                    }
                    finally
                    {
                        DestroyIcon(hIcon);
                    }
                }
            }
        }
        return IntPtr.Zero;
    }
}
