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

        // Dock over the whole lister window's bottom-right corner (same as the Total Commander plugin).
        // Extended frame bounds excludes the drop shadow, matching the visible edge.
        var listerHwnd = GetListerWindow(hwnd);
        RECT nativeRect;
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
