using System.Runtime.InteropServices;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;
using SwiftList.Plugins.DirectoryOpus.Win32;

namespace SwiftList.Plugins.DirectoryOpus;

public class DirectoryOpusInlineSearchAdapter : IInlineSearchAdapter
{
    public string Name => "Directory Opus";

    public bool IsFileExplorer => true;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    private const uint WM_COPYDATA = 0x004A;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (!PluginSettingsService.GetSetting("SwiftList.Plugins.DirectoryOpus", "EnableInlineSearch", true))
            return false;

        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName))
            return false;

        return processName.Equals("dopus", StringComparison.OrdinalIgnoreCase) &&
               className.Equals("dopus.lister", StringComparison.OrdinalIgnoreCase);
    }

    public bool CanTrigger(IntPtr focusedHwnd, string className)
    {
        if (focusedHwnd == IntPtr.Zero || string.IsNullOrEmpty(className))
            return false;

        return className.Equals("dopus.filedisplay", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("dopus.filedisplaycontainer", StringComparison.OrdinalIgnoreCase);
    }

    public string? GetSearchScope(IntPtr hwnd)
    {
        var collector = new DirectoryOpusPathCollector();
        var className = Win32Helper.GetClassName(hwnd);
        return collector.TryGetPath(hwnd, className, hwnd, className, "dopus");
    }

    private static bool RunDopusCommandViaCopyData(string command)
    {
        var dopusParent = FindWindow("DOpus.ParentWindow", "Directory Opus");
        if (dopusParent == IntPtr.Zero) return false;

        try
        {
            var cmdString = command + "\0";
            var bytes = System.Text.Encoding.Unicode.GetBytes(cmdString);
            var pinnedArray = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var cds = new COPYDATASTRUCT
                {
                    dwData = (IntPtr)0x14,
                    cbData = bytes.Length,
                    lpData = pinnedArray.AddrOfPinnedObject()
                };
                SendMessage(dopusParent, WM_COPYDATA, IntPtr.Zero, ref cds);
                return true;
            }
            finally
            {
                pinnedArray.Free();
            }
        }
        catch
        {
            return false;
        }
    }

    public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
    {
        try
        {
            var scope = GetSearchScope(hwnd);
            var parent = Path.GetDirectoryName(path);
            var isInCurrentFolder = !string.IsNullOrEmpty(scope) && string.Equals(parent?.TrimEnd('\\'), scope.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

            if (Directory.Exists(path))
            {
                if (RunDopusCommandViaCopyData($"Go \"{path}\""))
                {
                    return true;
                }
            }
            else if (File.Exists(path))
            {
                var filename = Path.GetFileName(path);
                if (isInCurrentFolder)
                {
                    RunDopusCommandViaCopyData($"Select \"{filename}\" DESELECTNOMATCH SETFOCUS");
                    return true;
                }
                else
                {
                    if (parent != null && Directory.Exists(parent))
                    {
                        if (RunDopusCommandViaCopyData($"Go \"{parent}\""))
                        {
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(200);
                                RunDopusCommandViaCopyData($"Select \"{filename}\" DESELECTNOMATCH SETFOCUS");
                            });
                            return true;
                        }
                    }
                }
            }
        }
        catch
        {
            // ponytail: ignore execution errors, fallback to default behavior
        }
        return false;
    }

    public void OnSelectionChanged(IntPtr hwnd, string path)
    {
        if (hwnd == IntPtr.Zero || string.IsNullOrEmpty(path)) return;
        var scope = GetSearchScope(hwnd);
        var parent = Path.GetDirectoryName(path);
        var isInCurrentFolder = !string.IsNullOrEmpty(scope) && string.Equals(parent?.TrimEnd('\\'), scope.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        if (isInCurrentFolder)
        {
            var filename = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(filename))
            {
                RunDopusCommandViaCopyData($"Select \"{filename}\" DESELECTNOMATCH SETFOCUS");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    private static IntPtr GetListerWindow(IntPtr hwnd)
    {
        var current = hwnd;
        while (current != IntPtr.Zero)
        {
            var className = Win32Helper.GetClassName(current);
            if (className.Equals("dopus.lister", StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }
            current = Win32Helper.GetParent(current);
        }
        return hwnd;
    }

    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero) return false;

        var listerHwnd = GetListerWindow(hwnd);
        var containers = Win32Helper.GetVisibleContainers(listerHwnd);
        var targetHwnd = listerHwnd;

        if (containers.Count > 0)
        {
            targetHwnd = containers[0];

            containers.Sort((a, b) =>
            {
                Win32Helper.TryGetWindowRect(a, out var rA);
                Win32Helper.TryGetWindowRect(b, out var rB);
                if (Math.Abs(rA.Left - rB.Left) > 10)
                {
                    return rA.Left.CompareTo(rB.Left);
                }
                return rA.Top.CompareTo(rB.Top);
            });

            var activeIndex = -1;
            for (var i = 0; i < containers.Count; i++)
            {
                if (Win32Helper.IsDescendant(containers[i], hwnd))
                {
                    activeIndex = i;
                    break;
                }
            }

            if (activeIndex == -1)
            {
                string? lastSideIndexStr;
                lock (DirectoryOpusPathCollector._lastActiveSides)
                {
                    DirectoryOpusPathCollector._lastActiveSides.TryGetValue(listerHwnd, out lastSideIndexStr);
                }

                if (lastSideIndexStr != null && int.TryParse(lastSideIndexStr, out var targetIndex) && targetIndex < containers.Count)
                {
                    activeIndex = targetIndex;
                }
            }

            if (activeIndex != -1 && activeIndex < containers.Count)
            {
                targetHwnd = containers[activeIndex];
            }
        }

        RECT nativeRect;
        if (targetHwnd != listerHwnd)
        {
            if (GetWindowRect(targetHwnd, out nativeRect))
            {
                rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
                return true;
            }
        }

        if (DwmGetWindowAttribute(listerHwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out nativeRect, Marshal.SizeOf<RECT>()) == 0)
        {
            rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }

        if (GetWindowRect(listerHwnd, out nativeRect))
        {
            rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }

        return false;
    }

    public bool CanEnterActionsMode(IntPtr hwnd) => true;
}
