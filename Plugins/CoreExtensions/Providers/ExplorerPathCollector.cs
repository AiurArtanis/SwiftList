using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.CoreExtensions.Providers
{
    public class ExplorerPathCollector : IActivePathCollector
    {
        public string Name => TranslationService.Get("Plugins_ExplorerTargetName");

        public string TargetName => TranslationService.Get("Plugins_ExplorerTargetName");

        public bool CanHandle(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;

            return className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) ||
                   className.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                   className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase);
        }

        public string? TryGetPath(IntPtr activeHwnd, string activeClassName, IntPtr windowHwnd, string windowClassName, string processName)
        {
            if (windowHwnd == IntPtr.Zero) return null;

            // Check if it is the Desktop
            if (IsDesktopWindow(windowHwnd, windowClassName))
            {
                try
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }
                catch
                {
                    return null;
                }
            }

            // Check if it is CabinetWClass (Windows Explorer)
            if (windowClassName.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase))
            {
                return GetActiveExplorerPath(windowHwnd);
            }

            return null;
        }

        #region Win32 API and COM Helper
        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        private static bool IsDesktopWindow(IntPtr hwnd, string className)
        {
            if (hwnd == GetShellWindow()) return true;

            if (className.Equals("Progman", StringComparison.OrdinalIgnoreCase))
                return true;

            if (className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr defView = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero)
                    return true;
            }

            return false;
        }

        private static string? GetActiveExplorerPath(IntPtr targetHwnd)
        {
            try
            {
                var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
                if (shellWindowsType == null) return null;

                dynamic shellWindows = Activator.CreateInstance(shellWindowsType)!;
                int count = shellWindows.Count;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic? window = shellWindows.Item(i);
                        if (window == null) continue;

                        IntPtr hwnd = (IntPtr)window.HWND;
                        if (hwnd == targetHwnd)
                        {
                            string path = window.Document.Folder.Self.Path;
                            if (!string.IsNullOrEmpty(path))
                            {
                                if (path.StartsWith("::") || path.Contains("::{") ||
                                    path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                                {
                                    return null;
                                }
                                return path;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }
        #endregion
    }
}
