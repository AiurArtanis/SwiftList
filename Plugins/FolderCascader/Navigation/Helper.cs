using System.IO;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.FolderCascader.Navigation;

public static class Helper
{
    public static IntPtr FolderHBitmap { get; private set; } = IntPtr.Zero;
    public static IntPtr FileHBitmap { get; private set; } = IntPtr.Zero;
    public static IntPtr ThisPcHBitmap { get; private set; } = IntPtr.Zero;
    public static IntPtr FavoritesHBitmap { get; private set; } = IntPtr.Zero;
    public static IntPtr HistoryHBitmap { get; private set; } = IntPtr.Zero;

    private static readonly object _iconLock = new();
    private static readonly Dictionary<string, IntPtr> _extensionIconCache = new(StringComparer.OrdinalIgnoreCase);

    public static List<string> GetHistoryPaths()
    {
        var historyFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwiftList",
            "search-history.txt");
        if (!File.Exists(historyFile))
            return new List<string>();
        try
        {
            return File.ReadLines(historyFile)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(30)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public static List<string> GetOpenedExplorerPaths()
    {
        var paths = new List<string>();
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                var shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic dShell = shell;
                    dynamic windows = dShell.Windows();
                    if (windows != null)
                    {
                        int count = windows.Count;
                        for (var i = 0; i < count; i++)
                        {
                            try
                            {
                                dynamic window = windows.Item(i);
                                if (window != null)
                                {
                                    dynamic w = window;
                                    if (w.Name == "File Explorer" || w.Name == "资源管理器")
                                    {
                                        var doc = w.Document as dynamic;
                                        if (doc != null)
                                        {
                                            var folder = doc.Folder as dynamic;
                                            if (folder != null)
                                            {
                                                var self = folder.Self as dynamic;
                                                if (self != null)
                                                {
                                                    string path = self.Path;
                                                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                                                        paths.Add(path);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        catch { }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void EnsureIcons()
    {
        if (FolderHBitmap != IntPtr.Zero && FileHBitmap != IntPtr.Zero)
            return;

        lock (_iconLock)
        {
            if (FolderHBitmap != IntPtr.Zero && FileHBitmap != IntPtr.Zero)
                return;

            try
            {
                FolderHBitmap = ShellIconLoader.GetIconHBitmap("dummy_folder", isDir: true);
                FileHBitmap = ShellIconLoader.GetIconHBitmap("dummy_file.txt", isDir: false);
                ThisPcHBitmap = ShellIconLoader.GetIconHBitmap("shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", isDir: true);
                FavoritesHBitmap = ShellIconLoader.GetIconHBitmap("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}", isDir: true);
                HistoryHBitmap = ShellIconLoader.GetIconHBitmap("shell:::{0DF44EAA-FF21-4412-8A65-721541B589AB}", isDir: true);
                if (HistoryHBitmap == IntPtr.Zero)
                    HistoryHBitmap = FileHBitmap;
            }
            catch (Exception ex)
            {
                Logger.Log($"[FolderCascader] Failed to create cached HBITMAP icons: {ex.Message}", LogLevel.Warn);
            }
        }
    }

    public static IntPtr GetFileIconHBitmap(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
        {
            return FileHBitmap;
        }

        lock (_iconLock)
        {
            if (_extensionIconCache.TryGetValue(ext, out var hBitmap))
            {
                return hBitmap;
            }

            try
            {
                var dummyFile = "dummy" + ext;
                var hBmp = ShellIconLoader.GetIconHBitmap(dummyFile, isDir: false);
                if (hBmp != IntPtr.Zero)
                {
                    _extensionIconCache[ext] = hBmp;
                    return hBmp;
                }
            }
            catch { }

            return FileHBitmap;
        }
    }
}
