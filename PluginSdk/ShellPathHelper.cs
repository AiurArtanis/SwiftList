using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SwiftList.PluginSdk
{
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

        private const uint SHGFI_DISPLAYNAME = 0x000000200;

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
                IntPtr res = SHGetFileInfo(physicalPath, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_DISPLAYNAME);
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
                    string specialPath = Environment.GetFolderPath(folderType);
                    if (string.IsNullOrEmpty(specialPath)) continue;

                    string dirName = Path.GetFileName(specialPath);
                    string localizedName = GetLocalizedFolderName(specialPath);

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
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string downloadsPath = Path.Combine(userProfile, "Downloads");
                if (Directory.Exists(downloadsPath))
                {
                    string dirName = Path.GetFileName(downloadsPath);
                    string localizedName = GetLocalizedFolderName(downloadsPath);

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
    }
}
