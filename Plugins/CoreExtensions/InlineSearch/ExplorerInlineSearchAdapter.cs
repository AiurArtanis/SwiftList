using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using SwiftList.PluginSdk;
using SwiftList.Plugins.CoreExtensions.Providers;

namespace SwiftList.Plugins.CoreExtensions.InlineSearch
{
    public class ExplorerInlineSearchAdapter : IInlineSearchAdapter
    {
        public string Name => TranslationService.Get("Plugins_ExplorerTargetName");

        public bool CanHandle(IntPtr hwnd, string className, string processName)
        {
            if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName))
                return false;

            return processName.Equals("explorer", StringComparison.OrdinalIgnoreCase) &&
                   (className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) ||
                    className.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                    className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase));
        }

        public bool CanTrigger(IntPtr focusedHwnd, string className)
        {
            if (focusedHwnd == IntPtr.Zero) return false;

            // Check if focus is in an Explorer file view (list, grid, etc.)
            IntPtr current = focusedHwnd;
            for (int depth = 0; depth < 12 && current != IntPtr.Zero; depth++)
            {
                var sbClass = new StringBuilder(128);
                if (GetClassName(current, sbClass, sbClass.Capacity) > 0)
                {
                    string cls = sbClass.ToString();
                    if (cls.Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
                        cls.Equals("SysListView32", StringComparison.OrdinalIgnoreCase) ||
                        cls.Equals("DirectUIHWND", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                current = GetParent(current);
            }
            return false;
        }

        public string? GetSearchScope(IntPtr hwnd)
        {
            var sbClass = new StringBuilder(256);
            GetClassName(hwnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();
            string processName = GetProcessName(hwnd);

            var collector = new ExplorerPathCollector();
            return collector.TryGetPath(hwnd, className, hwnd, className, processName);
        }

        public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
        {
            try
            {
                var sbClass = new StringBuilder(256);
                GetClassName(hwnd, sbClass, sbClass.Capacity);
                string className = sbClass.ToString();

                bool isDesktop = className.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                                 className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase);

                if (Directory.Exists(path) && !isDesktop)
                {
                    if (TryLocateInExistingExplorer(path, hwnd))
                    {
                        return true;
                    }
                }

                if (File.Exists(path) || Directory.Exists(path))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    };

                    if (File.Exists(path))
                    {
                        string? workingDirectory = Path.GetDirectoryName(path);
                        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
                        {
                            startInfo.WorkingDirectory = workingDirectory;
                        }
                    }

                    Process.Start(startInfo);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
        {
            rect = default;
            if (hwnd == IntPtr.Zero) return false;
            var nativeRect = new RECT();
            int result = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out nativeRect, Marshal.SizeOf<RECT>());
            if (result == 0)
            {
                rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
                return true;
            }
            if (GetWindowRect(hwnd, out nativeRect))
            {
                rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
                return true;
            }
            return false;
        }

        public bool CanEnterActionsMode(IntPtr hwnd)
        {
            return true;
        }

        #region Win32 API and COM Helper
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private string GetProcessName(IntPtr hwnd)
        {
            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != 0)
                {
                    using (var proc = Process.GetProcessById((int)pid))
                    {
                        return proc.ProcessName;
                    }
                }
            }
            catch { }
            return "explorer";
        }

        private bool TryLocateInExistingExplorer(string path, IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero) return false;
            try
            {
                dynamic? window = FindExplorerWindow(explorerHwnd);
                if (window == null) return false;

                string? targetFolder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
                    return false;

                window.Navigate2(targetFolder);

                if (File.Exists(path))
                {
                    SelectItemInExplorerLater(path, explorerHwnd);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private dynamic? FindExplorerWindow(IntPtr explorerHwnd)
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

                    if ((IntPtr)window.HWND == explorerHwnd)
                    {
                        return window;
                    }
                }
                catch { }
            }
            return null;
        }

        private async void SelectItemInExplorerLater(string path, IntPtr explorerHwnd)
        {
            await Task.Delay(250);
            try
            {
                dynamic? window = FindExplorerWindow(explorerHwnd);
                if (window == null) return;

                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) return;

                dynamic folder = window.Document.Folder;
                dynamic? item = folder.ParseName(name);
                if (item == null) return;

                const int svsiSelect = 0x1;
                const int svsiDeselectOthers = 0x4;
                const int svsiEnsureVisible = 0x8;
                window.Document.SelectItem(item, svsiSelect | svsiDeselectOthers | svsiEnsureVisible);
            }
            catch { }
        }
        #endregion
    }
}
