using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
            
            if (className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            IntPtr current = focusedHwnd;
            for (int depth = 0; depth < 12 && current != IntPtr.Zero; depth++)
            {
                var sbClass = new StringBuilder(128);
                if (ExplorerAdapterHelpers.GetClassName(current, sbClass, sbClass.Capacity) > 0)
                {
                    string cls = sbClass.ToString();
                    if (cls.Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
                        cls.Equals("SysListView32", StringComparison.OrdinalIgnoreCase) ||
                        cls.Equals("DirectUIHWND", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                current = ExplorerAdapterHelpers.GetParent(current);
            }

            return false;
        }

        public string? GetSearchScope(IntPtr hwnd)
        {
            var sbClass = new StringBuilder(256);
            ExplorerAdapterHelpers.GetClassName(hwnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();
            string processName = ExplorerAdapterHelpers.GetProcessName(hwnd);
            var collector = new ExplorerPathCollector();
            return collector.TryGetPath(hwnd, className, hwnd, className, processName);
        }

        public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
        {
            try
            {
                var sbClass = new StringBuilder(256);
                ExplorerAdapterHelpers.GetClassName(hwnd, sbClass, sbClass.Capacity);
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

        public void OnSelectionChanged(IntPtr hwnd, string path)
        {
            if (hwnd == IntPtr.Zero || string.IsNullOrEmpty(path)) return;
            try
            {
                dynamic? window = ExplorerAdapterHelpers.FindExplorerWindow(hwnd);
                if (window == null) return;
                string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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

        public void OnSearchFinished(IntPtr hwnd, bool executed)
        {
        }

        public System.Collections.Generic.IEnumerable<string> GetListItems(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) yield break;
            dynamic? shellWindows = null;
            try
            {
                var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
                if (shellWindowsType == null) yield break;
                shellWindows = Activator.CreateInstance(shellWindowsType)!;
            }
            catch { yield break; }

            int count = 0;
            try { count = shellWindows.Count; } catch { yield break; }

            for (int i = 0; i < count; i++)
            {
                dynamic? window = null;
                try
                {
                    window = shellWindows.Item(i);
                    if (window == null) continue;
                    var windowHwnd = new IntPtr(Convert.ToInt64(window.HWND));
                    if (windowHwnd != hwnd) continue;
                }
                catch { continue; }

                dynamic? folderItems = null;
                try { folderItems = window.Document.Folder.Items(); } catch { continue; }

                int itemCount = 0;
                try { itemCount = folderItems.Count; } catch { continue; }

                for (int j = 0; j < itemCount; j++)
                {
                    string path = string.Empty;
                    try
                    {
                        dynamic? fi = folderItems.Item(j);
                        if (fi == null) continue;
                        path = fi.Path;
                    }
                    catch { continue; }

                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (path.StartsWith("::", StringComparison.Ordinal)
                     || path.Contains("::{", StringComparison.Ordinal)
                     || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    yield return path;
                }
                break;
            }
        }

        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
        {
            rect = default;
            if (hwnd == IntPtr.Zero) return false;
            var nativeRect = new ExplorerAdapterHelpers.RECT();
            int result = ExplorerAdapterHelpers.DwmGetWindowAttribute(hwnd, ExplorerAdapterHelpers.DWMWA_EXTENDED_FRAME_BOUNDS, out nativeRect, Marshal.SizeOf<ExplorerAdapterHelpers.RECT>());
            if (result == 0)
            {
                rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
                return true;
            }

            if (ExplorerAdapterHelpers.GetWindowRect(hwnd, out nativeRect))
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

        private bool TryLocateInExistingExplorer(string path, IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero) return false;
            try
            {
                dynamic? window = ExplorerAdapterHelpers.FindExplorerWindow(explorerHwnd);
                if (window == null) return false;
                string? targetFolder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
                    return false;
                window.Navigate2(targetFolder);
                if (File.Exists(path))
                {
                    ExplorerAdapterHelpers.SelectItemInExplorerLater(path, explorerHwnd);
                }

                return true;
            }

            catch
            {
                return false;
            }
        }
    }
}
