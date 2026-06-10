using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.Core.Hook.InlineSearch
{
    internal static class ElevatedListControlHelper
    {
        public static List<string> GetListItems(IntPtr hwnd)
        {
            var result = new List<string>();
            if (hwnd == IntPtr.Zero) return result;

            var sbClass = new StringBuilder(256);
            ListSearchNativeMethods.GetClassName(hwnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();

            if (className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
            {
                int count = (int)ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (count <= 0) return result;

                for (int i = 0; i < count; i++)
                {
                    int textLen = (int)ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LB_GETTEXTLEN, (IntPtr)i, IntPtr.Zero);
                    if (textLen > 0)
                    {
                        var sb = new StringBuilder(textLen + 1);
                        ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LB_GETTEXT, (IntPtr)i, sb);
                        result.Add(sb.ToString());
                    }
                    else
                    {
                        result.Add(string.Empty);
                    }
                }
            }
            else if (className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase))
            {
                int count = (int)ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (count <= 0) return result;

                // Query multi-column headers
                int colCount = 1;
                IntPtr hwndHeader = ListSearchNativeMethods.SendMessage(hwnd, 0x1000 + 31, IntPtr.Zero, IntPtr.Zero); // LVM_GETHEADER
                if (hwndHeader != IntPtr.Zero)
                {
                    int headerItems = (int)ListSearchNativeMethods.SendMessage(hwndHeader, 0x1200 + 0, IntPtr.Zero, IntPtr.Zero); // HDM_GETITEMCOUNT
                    if (headerItems > 1)
                    {
                        colCount = Math.Min(headerItems, 10); // Cap at 10 columns for performance
                    }
                }

                ListSearchNativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                IntPtr hProcess = ListSearchNativeMethods.OpenProcess(
                    ListSearchNativeMethods.PROCESS_VM_OPERATION |
                    ListSearchNativeMethods.PROCESS_VM_READ |
                    ListSearchNativeMethods.PROCESS_VM_WRITE,
                    false, pid);
                if (hProcess == IntPtr.Zero) return result;

                uint lvItemSize = (uint)Marshal.SizeOf<ListSearchNativeMethods.LVITEM>();
                uint bufferSize = 512;
                IntPtr remoteLvItemPtr = ListSearchNativeMethods.VirtualAllocEx(hProcess, IntPtr.Zero, lvItemSize, ListSearchNativeMethods.MEM_COMMIT, ListSearchNativeMethods.PAGE_READWRITE);
                IntPtr remoteBufferPtr = ListSearchNativeMethods.VirtualAllocEx(hProcess, IntPtr.Zero, bufferSize, ListSearchNativeMethods.MEM_COMMIT, ListSearchNativeMethods.PAGE_READWRITE);

                if (remoteLvItemPtr != IntPtr.Zero && remoteBufferPtr != IntPtr.Zero)
                {
                    byte[] localBuffer = new byte[bufferSize];

                    for (int i = 0; i < count; i++)
                    {
                        var colTexts = new List<string>(colCount);
                        for (int col = 0; col < colCount; col++)
                        {
                            var item = new ListSearchNativeMethods.LVITEM
                            {
                                mask = ListSearchNativeMethods.LVIF_TEXT,
                                iItem = i,
                                iSubItem = col,
                                pszText = remoteBufferPtr,
                                cchTextMax = (int)bufferSize / 2
                            };

                            ListSearchNativeMethods.WriteProcessMemory(hProcess, remoteLvItemPtr, ref item, lvItemSize, out _);
                            ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LVM_GETITEMTEXTW, (IntPtr)i, remoteLvItemPtr);
                            ListSearchNativeMethods.ReadProcessMemory(hProcess, remoteBufferPtr, localBuffer, bufferSize, out _);

                            string text = Encoding.Unicode.GetString(localBuffer);
                            int nullIndex = text.IndexOf('\0');
                            if (nullIndex >= 0)
                            {
                                text = text.Substring(0, nullIndex);
                            }
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                colTexts.Add(text);
                            }
                        }
                        result.Add(string.Join("\t", colTexts));
                    }
                }

                if (remoteLvItemPtr != IntPtr.Zero) ListSearchNativeMethods.VirtualFreeEx(hProcess, remoteLvItemPtr, 0, ListSearchNativeMethods.MEM_RELEASE);
                if (remoteBufferPtr != IntPtr.Zero) ListSearchNativeMethods.VirtualFreeEx(hProcess, remoteBufferPtr, 0, ListSearchNativeMethods.MEM_RELEASE);
                ListSearchNativeMethods.CloseHandle(hProcess);
            }

            return result;
        }

        public static void SelectItem(IntPtr hwnd, string className, int index, bool clearOthers, bool selectState)
        {
            if (hwnd == IntPtr.Zero) return;

            int style = ListSearchNativeMethods.GetWindowLong(hwnd, ListSearchNativeMethods.GWL_STYLE);
            bool isMulti = false;
            if (className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
            {
                isMulti = (style & (int)ListSearchNativeMethods.LBS_MULTIPLESEL) != 0 || (style & (int)ListSearchNativeMethods.LBS_EXTENDEDSEL) != 0;
            }
            else if (className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase))
            {
                isMulti = (style & (int)ListSearchNativeMethods.LVS_SINGLESEL) == 0;
            }

            if (className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
            {
                if (isMulti)
                {
                    if (clearOthers)
                    {
                        ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LB_SETSEL, IntPtr.Zero, (IntPtr)(-1));
                    }

                    ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LB_SETSEL, (IntPtr)(selectState ? 1 : 0), (IntPtr)index);
                    if (selectState)
                    {
                        ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LB_SETCARETINDEX, (IntPtr)index, (IntPtr)1);
                    }

                    NotifyListBoxParent(hwnd, ListSearchNativeMethods.LBN_SELCHANGE);
                }
                else
                {
                    if (selectState)
                        ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LB_SETCURSEL, (IntPtr)index, IntPtr.Zero);
                    else
                        ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LB_SETCURSEL, (IntPtr)(-1), IntPtr.Zero);

                    NotifyListBoxParent(hwnd, ListSearchNativeMethods.LBN_SELCHANGE);
                }
            }
            else if (className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase))
            {
                ListSearchNativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                IntPtr hProcess = ListSearchNativeMethods.OpenProcess(ListSearchNativeMethods.PROCESS_VM_OPERATION | ListSearchNativeMethods.PROCESS_VM_WRITE, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    uint lvItemSize = (uint)Marshal.SizeOf<ListSearchNativeMethods.LVITEM>();
                    IntPtr remoteLvItemPtr = ListSearchNativeMethods.VirtualAllocEx(hProcess, IntPtr.Zero, lvItemSize, ListSearchNativeMethods.MEM_COMMIT, ListSearchNativeMethods.PAGE_READWRITE);

                    if (remoteLvItemPtr != IntPtr.Zero)
                    {
                        if (clearOthers)
                        {
                            var clearAllItem = new ListSearchNativeMethods.LVITEM
                            {
                                state = 0,
                                stateMask = ListSearchNativeMethods.LVIS_SELECTED
                            };
                            ListSearchNativeMethods.WriteProcessMemory(hProcess, remoteLvItemPtr, ref clearAllItem, lvItemSize, out _);
                            ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LVM_SETITEMSTATE, (IntPtr)(-1), remoteLvItemPtr);
                        }

                        var selectItem = new ListSearchNativeMethods.LVITEM
                        {
                            state = (selectState ? ListSearchNativeMethods.LVIS_SELECTED : 0) | (selectState ? ListSearchNativeMethods.LVIS_FOCUSED : 0),
                            stateMask = ListSearchNativeMethods.LVIS_SELECTED | (selectState ? ListSearchNativeMethods.LVIS_FOCUSED : 0)
                        };
                        ListSearchNativeMethods.WriteProcessMemory(hProcess, remoteLvItemPtr, ref selectItem, lvItemSize, out _);
                        ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LVM_SETITEMSTATE, (IntPtr)index, remoteLvItemPtr);

                        ListSearchNativeMethods.VirtualFreeEx(hProcess, remoteLvItemPtr, 0, ListSearchNativeMethods.MEM_RELEASE);
                    }
                    ListSearchNativeMethods.CloseHandle(hProcess);
                }
                if (selectState)
                {
                    ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LVM_ENSUREVISIBLE, (IntPtr)index, IntPtr.Zero);
                }
            }
        }

        public static void ClearSelection(IntPtr hwnd, string className)
        {
            if (hwnd == IntPtr.Zero) return;

            if (className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
            {
                ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LB_SETSEL, IntPtr.Zero, (IntPtr)(-1));
            }
            else if (className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase))
            {
                ListSearchNativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                IntPtr hProcess = ListSearchNativeMethods.OpenProcess(ListSearchNativeMethods.PROCESS_VM_OPERATION | ListSearchNativeMethods.PROCESS_VM_WRITE, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    uint lvItemSize = (uint)Marshal.SizeOf<ListSearchNativeMethods.LVITEM>();
                    IntPtr remoteLvItemPtr = ListSearchNativeMethods.VirtualAllocEx(hProcess, IntPtr.Zero, lvItemSize, ListSearchNativeMethods.MEM_COMMIT, ListSearchNativeMethods.PAGE_READWRITE);
                    if (remoteLvItemPtr != IntPtr.Zero)
                    {
                        var clearAllItem = new ListSearchNativeMethods.LVITEM
                        {
                            state = 0,
                            stateMask = ListSearchNativeMethods.LVIS_SELECTED | ListSearchNativeMethods.LVIS_FOCUSED
                        };
                        ListSearchNativeMethods.WriteProcessMemory(hProcess, remoteLvItemPtr, ref clearAllItem, lvItemSize, out _);
                        ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LVM_SETITEMSTATE, (IntPtr)(-1), remoteLvItemPtr);
                        ListSearchNativeMethods.VirtualFreeEx(hProcess, remoteLvItemPtr, 0, ListSearchNativeMethods.MEM_RELEASE);
                    }
                    ListSearchNativeMethods.CloseHandle(hProcess);
                }
            }
        }

        public static List<int> GetSelectedIndices(IntPtr hwnd, string className)
        {
            var selected = new List<int>();
            if (hwnd == IntPtr.Zero) return selected;

            if (className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
            {
                int style = ListSearchNativeMethods.GetWindowLong(hwnd, ListSearchNativeMethods.GWL_STYLE);
                bool isMulti = (style & (int)ListSearchNativeMethods.LBS_MULTIPLESEL) != 0 ||
                               (style & (int)ListSearchNativeMethods.LBS_EXTENDEDSEL) != 0;
                if (isMulti)
                {
                    int count = (int)ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero);
                    for (int i = 0; i < count; i++)
                    {
                        int sel = (int)ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LB_GETSEL, (IntPtr)i, IntPtr.Zero);
                        if (sel > 0)
                        {
                            selected.Add(i);
                        }
                    }
                }
                else
                {
                    int curSel = (int)ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);
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
                    index = (int)ListSearchNativeMethods.SendMessage(hwnd, ListSearchNativeMethods.LVM_GETNEXTITEM, (IntPtr)index, (IntPtr)ListSearchNativeMethods.LVNI_SELECTED);
                    if (index < 0) break;
                    selected.Add(index);
                }
            }
            return selected;
        }

        private static void NotifyListBoxParent(IntPtr hwnd, uint notificationCode)
        {
            IntPtr parent = ListSearchNativeMethods.GetParent(hwnd);
            if (parent == IntPtr.Zero) return;

            int ctrlId = ListSearchNativeMethods.GetWindowLong(hwnd, ListSearchNativeMethods.GWL_ID);
            IntPtr wParam = (IntPtr)(((int)notificationCode << 16) | (ctrlId & 0xFFFF));
            ListSearchNativeMethods.SendMessage(parent, ListSearchNativeMethods.WM_COMMAND, wParam, hwnd);
        }
    }
}
