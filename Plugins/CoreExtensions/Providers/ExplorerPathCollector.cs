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
        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
        private interface IComServiceProvider
        {
            [PreserveSig]
            int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214E2-0000-0000-C000-000000000046")]
        private interface IShellBrowser
        {
            [PreserveSig]
            int GetWindow(out IntPtr phwnd);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

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
                // Find the first ShellTabWindowClass in Z-order
                IntPtr activeTabHwnd = IntPtr.Zero;
                EnumChildWindows(targetHwnd, (childHwnd, lParam) =>
                {
                    var sbChildClass = new StringBuilder(256);
                    GetClassName(childHwnd, sbChildClass, sbChildClass.Capacity);
                    string childClass = sbChildClass.ToString();

                    if (childClass.Equals("ShellTabWindowClass", StringComparison.OrdinalIgnoreCase))
                    {
                        activeTabHwnd = childHwnd;
                        return false; // Stop enumeration immediately
                    }
                    return true;
                }, IntPtr.Zero);

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
                            // If we identified an active tab in the UI, check if this COM window matches it
                            if (activeTabHwnd != IntPtr.Zero)
                            {
                                if (window is IComServiceProvider serviceProvider)
                                {
                                    Guid serviceId = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
                                    Guid interfaceId = new Guid("000214E2-0000-0000-C000-000000000046"); // IID_IShellBrowser
                                    
                                    int hr = serviceProvider.QueryService(ref serviceId, ref interfaceId, out IntPtr shellBrowserPtr);
                                    if (hr == 0 && shellBrowserPtr != IntPtr.Zero)
                                    {
                                        var shellBrowser = (IShellBrowser)Marshal.GetObjectForIUnknown(shellBrowserPtr);
                                        shellBrowser.GetWindow(out IntPtr tabHwnd);
                                        Marshal.Release(shellBrowserPtr);

                                        if (tabHwnd != activeTabHwnd)
                                        {
                                            continue; // Not the active tab
                                        }
                                    }
                                }
                            }

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
