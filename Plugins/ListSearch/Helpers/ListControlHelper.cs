using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

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
            var result = new List<string>();

            if (className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
            {
                int count = (int)Win32Api.SendMessage(hwnd, Win32Api.LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (count <= 0) return result;

                for (int i = 0; i < count; i++)
                {
                    int textLen = (int)Win32Api.SendMessage(hwnd, Win32Api.LB_GETTEXTLEN, (IntPtr)i, IntPtr.Zero);
                    if (textLen > 0)
                    {
                        var sb = new StringBuilder(textLen + 1);
                        Win32Api.SendMessage(hwnd, Win32Api.LB_GETTEXT, (IntPtr)i, sb);
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
                int count = (int)Win32Api.SendMessage(hwnd, Win32Api.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (count <= 0) return result;

                Win32Api.GetWindowThreadProcessId(hwnd, out uint pid);
                IntPtr hProcess = Win32Api.OpenProcess(Win32Api.PROCESS_VM_OPERATION | Win32Api.PROCESS_VM_READ | Win32Api.PROCESS_VM_WRITE, false, pid);
                if (hProcess == IntPtr.Zero) return result;

                uint lvItemSize = (uint)Marshal.SizeOf<Win32Api.LVITEM>();
                uint bufferSize = 512;
                IntPtr remoteLvItemPtr = Win32Api.VirtualAllocEx(hProcess, IntPtr.Zero, lvItemSize, Win32Api.MEM_COMMIT, Win32Api.PAGE_READWRITE);
                IntPtr remoteBufferPtr = Win32Api.VirtualAllocEx(hProcess, IntPtr.Zero, bufferSize, Win32Api.MEM_COMMIT, Win32Api.PAGE_READWRITE);

                if (remoteLvItemPtr != IntPtr.Zero && remoteBufferPtr != IntPtr.Zero)
                {
                    byte[] localBuffer = new byte[bufferSize];

                    for (int i = 0; i < count; i++)
                    {
                        var item = new Win32Api.LVITEM
                        {
                            mask = Win32Api.LVIF_TEXT,
                            iItem = i,
                            iSubItem = 0,
                            pszText = remoteBufferPtr,
                            cchTextMax = (int)bufferSize / 2
                        };

                        Win32Api.WriteProcessMemory(hProcess, remoteLvItemPtr, ref item, lvItemSize, out _);
                        Win32Api.SendMessage(hwnd, Win32Api.LVM_GETITEMTEXTW, (IntPtr)i, remoteLvItemPtr);
                        Win32Api.ReadProcessMemory(hProcess, remoteBufferPtr, localBuffer, bufferSize, out _);

                        string text = Encoding.Unicode.GetString(localBuffer);
                        int nullIndex = text.IndexOf('\0');
                        if (nullIndex >= 0)
                        {
                            text = text.Substring(0, nullIndex);
                        }
                        result.Add(text);
                    }
                }

                if (remoteLvItemPtr != IntPtr.Zero) Win32Api.VirtualFreeEx(hProcess, remoteLvItemPtr, 0, Win32Api.MEM_RELEASE);
                if (remoteBufferPtr != IntPtr.Zero) Win32Api.VirtualFreeEx(hProcess, remoteBufferPtr, 0, Win32Api.MEM_RELEASE);
                Win32Api.CloseHandle(hProcess);
            }

            return result;
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
            bool isMulti = IsMultiSelect(hwnd, className);

            if (className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
            {
                if (isMulti)
                {
                    if (clearOthers)
                    {
                        Win32Api.SendMessage(hwnd, Win32Api.LB_SETSEL, IntPtr.Zero, (IntPtr)(-1));
                    }
                    Win32Api.SendMessage(hwnd, Win32Api.LB_SETSEL, (IntPtr)(selectState ? 1 : 0), (IntPtr)index);
                    if (selectState)
                    {
                        Win32Api.SendMessage(hwnd, Win32Api.LB_SETCARETINDEX, (IntPtr)index, (IntPtr)1);
                    }
                }
                else
                {
                    if (selectState)
                    {
                        Win32Api.SendMessage(hwnd, Win32Api.LB_SETCURSEL, (IntPtr)index, IntPtr.Zero);
                    }
                    else
                    {
                        Win32Api.SendMessage(hwnd, Win32Api.LB_SETCURSEL, (IntPtr)(-1), IntPtr.Zero);
                    }
                }
            }
            else if (className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase))
            {
                Win32Api.GetWindowThreadProcessId(hwnd, out uint pid);
                IntPtr hProcess = Win32Api.OpenProcess(Win32Api.PROCESS_VM_OPERATION | Win32Api.PROCESS_VM_WRITE, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    uint lvItemSize = (uint)Marshal.SizeOf<Win32Api.LVITEM>();
                    IntPtr remoteLvItemPtr = Win32Api.VirtualAllocEx(hProcess, IntPtr.Zero, lvItemSize, Win32Api.MEM_COMMIT, Win32Api.PAGE_READWRITE);

                    if (remoteLvItemPtr != IntPtr.Zero)
                    {
                        if (isMulti)
                        {
                            if (clearOthers)
                            {
                                var clearAllItem = new Win32Api.LVITEM
                                {
                                    state = 0,
                                    stateMask = Win32Api.LVIS_SELECTED
                                };
                                Win32Api.WriteProcessMemory(hProcess, remoteLvItemPtr, ref clearAllItem, lvItemSize, out _);
                                Win32Api.SendMessage(hwnd, Win32Api.LVM_SETITEMSTATE, (IntPtr)(-1), remoteLvItemPtr);
                            }

                            var selectItem = new Win32Api.LVITEM
                            {
                                state = (selectState ? Win32Api.LVIS_SELECTED : 0) | (selectState ? Win32Api.LVIS_FOCUSED : 0),
                                stateMask = Win32Api.LVIS_SELECTED | (selectState ? Win32Api.LVIS_FOCUSED : 0)
                            };
                            Win32Api.WriteProcessMemory(hProcess, remoteLvItemPtr, ref selectItem, lvItemSize, out _);
                            Win32Api.SendMessage(hwnd, Win32Api.LVM_SETITEMSTATE, (IntPtr)index, remoteLvItemPtr);
                        }
                        else
                        {
                            if (clearOthers)
                            {
                                var clearAllItem = new Win32Api.LVITEM
                                {
                                    state = 0,
                                    stateMask = Win32Api.LVIS_SELECTED
                                };
                                Win32Api.WriteProcessMemory(hProcess, remoteLvItemPtr, ref clearAllItem, lvItemSize, out _);
                                Win32Api.SendMessage(hwnd, Win32Api.LVM_SETITEMSTATE, (IntPtr)(-1), remoteLvItemPtr);
                            }

                            var selectItem = new Win32Api.LVITEM
                            {
                                state = (selectState ? Win32Api.LVIS_SELECTED : 0) | (selectState ? Win32Api.LVIS_FOCUSED : 0),
                                stateMask = Win32Api.LVIS_SELECTED | (selectState ? Win32Api.LVIS_FOCUSED : 0)
                            };
                            Win32Api.WriteProcessMemory(hProcess, remoteLvItemPtr, ref selectItem, lvItemSize, out _);
                            Win32Api.SendMessage(hwnd, Win32Api.LVM_SETITEMSTATE, (IntPtr)index, remoteLvItemPtr);
                        }

                        Win32Api.VirtualFreeEx(hProcess, remoteLvItemPtr, 0, Win32Api.MEM_RELEASE);
                    }
                    Win32Api.CloseHandle(hProcess);
                }
                if (selectState)
                {
                    Win32Api.SendMessage(hwnd, Win32Api.LVM_ENSUREVISIBLE, (IntPtr)index, IntPtr.Zero);
                }
            }
        }

        public static void PostEnterKey(IntPtr hwnd)
        {
            Win32Api.PostMessage(hwnd, Win32Api.WM_KEYDOWN, (IntPtr)Win32Api.VK_RETURN, (IntPtr)0);
            Win32Api.PostMessage(hwnd, Win32Api.WM_KEYUP, (IntPtr)Win32Api.VK_RETURN, (IntPtr)0);
        }
    }
}
