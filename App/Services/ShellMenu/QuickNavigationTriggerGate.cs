using System.Runtime.InteropServices;
using System.Text;
using SwiftList.PluginSdk.Abstractions.Plugins;
using Native = SwiftList.Core.Hook.ExplorerNativeHooks;
using PointNative = SwiftList.App.Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods;

namespace SwiftList.App.Services;

// Decides whether the Quick Navigation popup should open for a double-click/middle-click in Explorer,
// the desktop, or a recognized third-party file manager. This used to live inside the FolderCascader
// plugin (as its CanShow), but none of it is actually FolderCascader-specific content-provider logic --
// it's host recognition, the same kind of thing IInlineSearchAdapter/IFileDialogAdapter already do for
// their hosts, so it belongs here alongside FileDialogQuickNavGate rather than behind a plugin interface.
internal static class QuickNavigationTriggerGate
{
    public static bool CanShow(IntPtr activeHwnd, string processName, string className, bool isDesktop, int x, int y, MouseTriggerType triggerType)
    {
        if (string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase) || isDesktop)
        {
            return CanShowInExplorer(activeHwnd, x, y);
        }

        return CanShowInOtherFileManager(activeHwnd, processName, className, x, y, triggerType);
    }

    private static bool CanShowInExplorer(IntPtr activeHwnd, int x, int y)
    {
        var hwndUnderCursor = PointNative.WindowFromPoint(new PointNative.POINT { x = x, y = y });
        if (hwndUnderCursor == IntPtr.Zero) return false;

        if (!IsDescendantOfShellDllDefView(hwndUnderCursor)) return false;

        var sbClass = new StringBuilder(256);
        Native.GetClassName(hwndUnderCursor, sbClass, sbClass.Capacity);
        var clsName = sbClass.ToString();

        if (!clsName.Equals("DirectUIHWND", StringComparison.OrdinalIgnoreCase) &&
            !clsName.Equals("SysListView32", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (clsName.Equals("SysListView32", StringComparison.OrdinalIgnoreCase))
        {
            // Cross-process LVM_HITTEST distinguishes a desktop icon from empty space; if that fails
            // (process open/memory allocation failure), fall through to the Shell selection-count check.
            if (IsPointOnDesktopIcon(hwndUnderCursor, x, y))
            {
                return false;
            }
        }

        return IsActiveWindowFolderEmptySpace(activeHwnd);
    }

    // Third-party file managers (Directory Opus, Total Commander, ...) integrate through their
    // IInlineSearchAdapter instead of host-specific hit-testing here -- CanShowQuickNav reuses whatever
    // "is this the host's file list" check the adapter already has for inline search's keyboard trigger.
    //
    // Restricted to middle-click: unlike Explorer (where empty space is detected precisely via the shell's
    // selection count), these hosts give no reliable way to tell "clicked an item" from "clicked empty
    // space", and double-clicking an item there already navigates into it -- popping this menu on top of
    // that would be confusing. Middle-click carries no such default action in these hosts.
    private static bool CanShowInOtherFileManager(IntPtr activeHwnd, string processName, string className, int x, int y, MouseTriggerType triggerType)
    {
        if (triggerType != MouseTriggerType.MiddleClick) return false;

        var adapter = PluginSdk.Registries.InlineSearchAdapterRegistry.GetMatchingAdapter(activeHwnd, className, processName);
        if (adapter == null || !adapter.IsFileExplorer) return false;

        var hwndUnderCursor = PointNative.WindowFromPoint(new PointNative.POINT { x = x, y = y });
        if (hwndUnderCursor == IntPtr.Zero) return false;

        // Same staleness guard as FileDialogQuickNavGate: activeHwnd tracks the OS foreground window, which
        // a middle-click doesn't change, so a stale match could otherwise pass a completely unrelated
        // window's class name (e.g. the desktop's) to an adapter whose own CanShowQuickNav happens not to
        // reject it. Require the clicked window to actually be inside the matched host window first.
        if (Native.GetAncestor(hwndUnderCursor, GA_ROOT) != activeHwnd) return false;

        var sbClass = new StringBuilder(256);
        Native.GetClassName(hwndUnderCursor, sbClass, sbClass.Capacity);
        return adapter.CanShowQuickNav(hwndUnderCursor, sbClass.ToString());
    }

    private const uint GA_ROOT = 2;

    private static bool IsDescendantOfShellDllDefView(IntPtr hwnd)
    {
        var current = hwnd;
        while (current != IntPtr.Zero)
        {
            var sbClass = new StringBuilder(256);
            Native.GetClassName(current, sbClass, sbClass.Capacity);
            if (sbClass.ToString().Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            current = Native.GetParent(current);
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LVHITTESTINFO
    {
        public PointNative.POINT pt;
        public uint flags;
        public int iItem;
        public int iSubItem;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref PointNative.POINT lpPoint);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, ref LVHITTESTINFO lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, out LVHITTESTINFO lpBuffer, uint nSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static bool IsPointOnDesktopIcon(IntPtr hwndListView, int x, int y)
    {
        var hProcess = IntPtr.Zero;
        var pRemoteMem = IntPtr.Zero;
        try
        {
            Native.GetWindowThreadProcessId(hwndListView, out var pid);
            hProcess = OpenProcess(0x001F0FFF /* PROCESS_ALL_ACCESS */, false, pid);
            if (hProcess == IntPtr.Zero) return false;

            var pt = new PointNative.POINT { x = x, y = y };
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

            PointNative.SendMessage(hwndListView, 0x1012 /* LVM_HITTEST */, IntPtr.Zero, pRemoteMem);

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

    private static bool IsActiveWindowFolderEmptySpace(IntPtr hwnd)
    {
        try
        {
            var rootHwnd = Native.GetAncestor(hwnd, GA_ROOT);
            var isActiveDesktop = Native.IsDesktopWindow(rootHwnd, out _);

            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return true;

            var shell = Activator.CreateInstance(shellType);
            if (shell == null) return true;

            dynamic dShell = shell;
            dynamic windows = dShell.Windows();
            if (windows == null) return true;

            int count = windows.Count;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    if (window == null) continue;

                    dynamic w = window;
                    var wHwnd = new IntPtr(w.HWND);

                    var isMatch = isActiveDesktop ? Native.IsDesktopWindow(wHwnd, out _) : wHwnd == rootHwnd;
                    if (!isMatch) continue;

                    dynamic doc = w.Document;
                    if (doc != null)
                    {
                        dynamic selectedItems = doc.SelectedItems;
                        if (selectedItems != null)
                        {
                            int itemsCount = selectedItems.Count;
                            if (itemsCount > 0) return false;
                        }
                    }
                    break;
                }
                catch { }
            }
        }
        catch { }
        return true;
    }
}
