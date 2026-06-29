using System.Runtime.InteropServices;
using SwiftList.PluginSdk.Models;

namespace SwiftList.Plugins.ListSearch.Helpers;

internal static class ListControlHelper
{
    public static IntPtr GetFocusedControl(IntPtr parentHwnd)
    {
        try
        {
            var threadId = Win32Api.GetWindowThreadProcessId(parentHwnd, out _);
            var guiInfo = new Win32Api.GUITHREADINFO();
            guiInfo.cbSize = Marshal.SizeOf(guiInfo);
            if (Win32Api.GetGUIThreadInfo(threadId, ref guiInfo) && guiInfo.hwndFocus != IntPtr.Zero)
            {
                return guiInfo.hwndFocus;
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    public static IEnumerable<string> GetListItemsInternal(IntPtr hwnd, string className)
    {
        if (ListControlIpcBridge.GetListItemsFunc != null)
        {
            return ListControlIpcBridge.GetListItemsFunc(hwnd);
        }
        return Array.Empty<string>();
    }

    public static bool IsMultiSelect(IntPtr hwnd, string className)
    {
        var style = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_STYLE);
        if (className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
        {
            return (style & (int)Win32Api.LBS_MULTIPLESEL) != 0 || (style & (int)Win32Api.LBS_EXTENDEDSEL) != 0;
        }
        if (className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase))
        {
            return (style & (int)Win32Api.LVS_SINGLESEL) == 0;
        }
        return false;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern int SendMessageGetSelItems(IntPtr hWnd, uint Msg, IntPtr wParam, int[] lParam);

    private const uint LB_GETSELCOUNT = 0x0190;
    private const uint LB_GETSELITEMS = 0x0191;

    public static HashSet<int> GetSelectedIndices(IntPtr hwnd, string className)
    {
        if (ListControlIpcBridge.GetSelectedIndicesFunc != null)
        {
            var result = ListControlIpcBridge.GetSelectedIndicesFunc(hwnd, className);
            return new HashSet<int>(result);
        }

        var selected = new HashSet<int>();
        if (className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
        {
            var isMulti = IsMultiSelect(hwnd, className);
            if (isMulti)
            {
                var selCount = (int)Win32Api.SendMessage(hwnd, LB_GETSELCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (selCount > 0)
                {
                    var indices = new int[selCount];
                    var retrieved = SendMessageGetSelItems(hwnd, LB_GETSELITEMS, (IntPtr)selCount, indices);
                    if (retrieved > 0)
                    {
                        for (var i = 0; i < retrieved; i++)
                        {
                            selected.Add(indices[i]);
                        }
                    }
                }
            }
            else
            {
                var curSel = (int)Win32Api.SendMessage(hwnd, Win32Api.LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);
                if (curSel >= 0)
                {
                    selected.Add(curSel);
                }
            }
        }
        else if (className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase))
        {
            var index = -1;
            while (true)
            {
                index = (int)Win32Api.SendMessage(hwnd, Win32Api.LVM_GETNEXTITEM, (IntPtr)index, (IntPtr)Win32Api.LVNI_SELECTED);
                if (index < 0) break;
                selected.Add(index);
            }
        }
        return selected;
    }

    public static void SelectItem(IntPtr hwnd, string className, int index, bool clearOthers, bool selectState) => ListControlIpcBridge.SelectItemAction?.Invoke(hwnd, className, index, clearOthers, selectState);

    public static void ClearSelection(IntPtr hwnd, string className) => ListControlIpcBridge.ClearSelectionAction?.Invoke(hwnd, className);
}
