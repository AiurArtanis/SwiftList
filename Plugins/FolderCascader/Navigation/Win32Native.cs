using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.Plugins.FolderCascader.Navigation;

internal static class Win32Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LVHITTESTINFO
    {
        public POINT pt;
        public uint flags;
        public int iItem;
        public int iSubItem;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto)]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    public const uint GA_ROOT = 2;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, ref LVHITTESTINFO lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, out LVHITTESTINFO lpBuffer, uint nSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    public static bool IsDesktopWindow(IntPtr hwnd, string className)
    {
        if (hwnd == GetShellWindow()) return true;
        if (className.Equals("Progman", StringComparison.OrdinalIgnoreCase)) return true;
        if (className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase))
        {
            return FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
        }
        return false;
    }

    public static bool IsDescendantOfShellDllDefView(IntPtr hwnd)
    {
        var current = hwnd;
        while (current != IntPtr.Zero)
        {
            var sbClass = new StringBuilder(256);
            GetClassName(current, sbClass, sbClass.Capacity);
            if (sbClass.ToString().Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            current = GetParent(current);
        }
        return false;
    }

    public static bool IsPointOnDesktopIcon(IntPtr hwndListView, int x, int y)
    {
        var hProcess = IntPtr.Zero;
        var pRemoteMem = IntPtr.Zero;
        try
        {
            GetWindowThreadProcessId(hwndListView, out var pid);
            hProcess = OpenProcess(0x001F0FFF /* PROCESS_ALL_ACCESS */, false, (int)pid);
            if (hProcess == IntPtr.Zero) return false;

            var pt = new POINT(x, y);
            ScreenToClient(hwndListView, ref pt);

            var hitTestInfo = new LVHITTESTINFO
            {
                pt = pt,
                flags = 0,
                iItem = -1,
                iSubItem = -1
            };

            pRemoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)Marshal.SizeOf<LVHITTESTINFO>(), 0x1000 /* MEM_COMMIT */, 0x04 /* PAGE_READWRITE */);
            if (pRemoteMem == IntPtr.Zero) return false;

            WriteProcessMemory(hProcess, pRemoteMem, ref hitTestInfo, (uint)Marshal.SizeOf<LVHITTESTINFO>(), out _);

            SendMessage(hwndListView, 0x1012 /* LVM_HITTEST */, IntPtr.Zero, pRemoteMem);

            ReadProcessMemory(hProcess, pRemoteMem, out hitTestInfo, (uint)Marshal.SizeOf<LVHITTESTINFO>(), out _);

            return hitTestInfo.iItem != -1;
        }
        catch { }
        finally
        {
            if (pRemoteMem != IntPtr.Zero) VirtualFreeEx(hProcess, pRemoteMem, 0, 0x8000 /* MEM_RELEASE */);
            if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
        }
        return false;
    }

    public static bool IsActiveWindowFolderEmptySpace(IntPtr hwnd)
    {
        try
        {
            var rootHwnd = GetAncestor(hwnd, GA_ROOT);
            var sbClass = new StringBuilder(256);
            GetClassName(rootHwnd, sbClass, sbClass.Capacity);
            var rootClassName = sbClass.ToString();

            var isActiveDesktop = IsDesktopWindow(rootHwnd, rootClassName);

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
                                    var wHwnd = new IntPtr(w.HWND);
                                    var sbWClass = new StringBuilder(256);
                                    GetClassName(wHwnd, sbWClass, sbWClass.Capacity);
                                    var wClassName = sbWClass.ToString();

                                    var isMatch = false;
                                    if (isActiveDesktop)
                                    {
                                        isMatch = IsDesktopWindow(wHwnd, wClassName);
                                    }
                                    else
                                    {
                                        isMatch = (wHwnd == rootHwnd);
                                    }

                                    if (isMatch)
                                    {
                                        dynamic doc = w.Document;
                                        if (doc != null)
                                        {
                                            dynamic selectedItems = doc.SelectedItems;
                                            if (selectedItems != null)
                                            {
                                                int itemsCount = selectedItems.Count;
                                                if (itemsCount > 0)
                                                {
                                                    return false;
                                                }
                                            }
                                        }
                                        break;
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
        return true;
    }
}
