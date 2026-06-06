using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.ListSearch.Helpers
{
    internal static class ListControlHelper
    {
        public static IntPtr GetFocusedControl(IntPtr parentHwnd)
        {
            try
            {
                uint threadId = Win32Api.GetWindowThreadProcessId(parentHwnd, out _);
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

        public static List<string> GetListItemsInternal(IntPtr hwnd, string className)
        {
            if (ListControlIpcBridge.GetListItemsFunc != null)
            {
                var items = ListControlIpcBridge.GetListItemsFunc(hwnd);
                return new List<string>(items);
            }
            return new List<string>();
        }

        public static bool IsMultiSelect(IntPtr hwnd, string className)
        {
            int style = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_STYLE);
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
                bool isMulti = IsMultiSelect(hwnd, className);
                if (isMulti)
                {
                    int count = (int)Win32Api.SendMessage(hwnd, Win32Api.LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero);
                    for (int i = 0; i < count; i++)
                    {
                        int sel = (int)Win32Api.SendMessage(hwnd, Win32Api.LB_GETSEL, (IntPtr)i, IntPtr.Zero);
                        if (sel > 0)
                        {
                            selected.Add(i);
                        }
                    }
                }
                else
                {
                    int curSel = (int)Win32Api.SendMessage(hwnd, Win32Api.LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);
                    if (curSel >= 0)
                    {
                        selected.Add(curSel);
                    }
                }
            }
            else if (className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase))
            {
                int index = -1;
                while (true)
                {
                    index = (int)Win32Api.SendMessage(hwnd, Win32Api.LVM_GETNEXTITEM, (IntPtr)index, (IntPtr)Win32Api.LVNI_SELECTED);
                    if (index < 0) break;
                    selected.Add(index);
                }
            }
            return selected;
        }

        public static void SelectItem(IntPtr hwnd, string className, int index, bool clearOthers, bool selectState)
        {
            ListControlIpcBridge.SelectItemAction?.Invoke(hwnd, className, index, clearOthers, selectState);
        }

        public static void ClearSelection(IntPtr hwnd, string className)
        {
            ListControlIpcBridge.ClearSelectionAction?.Invoke(hwnd, className);
        }
    }
}
