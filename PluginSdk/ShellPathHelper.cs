using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.PluginSdk;

/// <summary>
/// Utility class for resolving Windows shell folders, localized paths, and virtual folders.
/// Shared with plugins via the SDK.
/// </summary>
public static class ShellPathHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfo", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDListW(IntPtr pidl, [Out] StringBuilder pszPath);

    private const uint SHGFI_DISPLAYNAME = 0x000000200;
    private const uint SHGFI_PIDL = 0x000000008;

    private static readonly Environment.SpecialFolder[] _trackedSpecialFolders = new[]
    {
        Environment.SpecialFolder.Desktop,
        Environment.SpecialFolder.MyDocuments,
        Environment.SpecialFolder.MyPictures,
        Environment.SpecialFolder.MyMusic,
        Environment.SpecialFolder.MyVideos,
        Environment.SpecialFolder.UserProfile
    };

    /// <summary>
    /// Retrieves the localized user-friendly display name of a physical folder.
    /// </summary>
    public static string GetLocalizedFolderName(string physicalPath)
    {
        try
        {
            var shfi = new SHFILEINFO();
            var res = SHGetFileInfo(physicalPath, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_DISPLAYNAME);
            if (res != IntPtr.Zero && !string.IsNullOrEmpty(shfi.szDisplayName))
            {
                return shfi.szDisplayName.Trim();
            }
        }
        catch { }
        return Path.GetFileName(physicalPath) ?? string.Empty;
    }

    /// <summary>
    /// Resolves a localized folder name (e.g. "Desktop", "Downloads") to its absolute physical path.
    /// </summary>
    public static string ResolveSpecialFolder(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        name = name.Trim();

        foreach (var folderType in _trackedSpecialFolders)
        {
            try
            {
                var specialPath = Environment.GetFolderPath(folderType);
                if (string.IsNullOrEmpty(specialPath)) continue;

                var dirName = Path.GetFileName(specialPath);
                var localizedName = GetLocalizedFolderName(specialPath);

                if (string.Equals(name, dirName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, localizedName, StringComparison.OrdinalIgnoreCase))
                {
                    return specialPath;
                }
            }
            catch { }
        }

        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloadsPath = Path.Combine(userProfile, "Downloads");
            if (Directory.Exists(downloadsPath))
            {
                var dirName = Path.GetFileName(downloadsPath);
                var localizedName = GetLocalizedFolderName(downloadsPath);

                if (string.Equals(name, dirName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, localizedName, StringComparison.OrdinalIgnoreCase))
                {
                    return downloadsPath;
                }
            }
        }
        catch { }

        return name;
    }

    /// <summary>
    /// Dynamically resolves a Windows shell virtual path (e.g. ::{450d8fba-...} or shell:::{...}) to its physical folder path.
    /// Returns the original path if it cannot be resolved.
    /// </summary>
    public static string TryResolveVirtualPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        if (path.StartsWith("::") || path.StartsWith("shell:"))
        {
            var pidl = IntPtr.Zero;
            try
            {
                var hr = SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _);
                if (hr == 0 && pidl != IntPtr.Zero)
                {
                    var sb = new StringBuilder(260);
                    if (SHGetPathFromIDListW(pidl, sb))
                    {
                        var resolved = sb.ToString();
                        if (!string.IsNullOrEmpty(resolved) && (Directory.Exists(resolved) || File.Exists(resolved)))
                        {
                            return resolved;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (pidl != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }
        return path;
    }

    /// <summary>
    /// Dynamically retrieves the localized user-friendly display name of a Windows shell virtual folder.
    /// </summary>
    public static string GetVirtualFolderDisplayName(string path, string fallback)
    {
        if (string.IsNullOrEmpty(path)) return fallback;

        var pidl = IntPtr.Zero;
        try
        {
            var hr = SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _);
            if (hr == 0 && pidl != IntPtr.Zero)
            {
                var shfi = new SHFILEINFO();
                var res = SHGetFileInfoPidl(pidl, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_DISPLAYNAME | SHGFI_PIDL);
                if (res != IntPtr.Zero && !string.IsNullOrEmpty(shfi.szDisplayName))
                {
                    return shfi.szDisplayName.Trim();
                }
            }
        }
        catch { }
        finally
        {
            if (pidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
        return fallback;
    }
}
