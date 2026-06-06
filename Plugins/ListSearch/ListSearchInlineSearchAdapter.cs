using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using SwiftList.PluginSdk;
using SwiftList.Plugins.ListSearch.Helpers;

namespace SwiftList.Plugins.ListSearch
{
    public class ListSearchInlineSearchAdapter : IInlineSearchAdapter
    {
        public string Name => TranslationService.Get("Plugins_ListSearchTargetName");

        private readonly HashSet<int> _originallySelectedIndices = new HashSet<int>();
        private IntPtr _lastHwnd = IntPtr.Zero;
        private int _lastPreviewIndex = -1;

        public bool CanHandle(IntPtr hwnd, string className, string processName)
        {
            if (string.IsNullOrEmpty(className)) return false;

            return className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase) ||
                   className.Contains("ListBox", StringComparison.OrdinalIgnoreCase);
        }

        public bool CanTrigger(IntPtr focusedHwnd, string className)
        {
            if (focusedHwnd == IntPtr.Zero || string.IsNullOrEmpty(className)) 
                return false;

            return className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase) ||
                   className.Contains("ListBox", StringComparison.OrdinalIgnoreCase);
        }

        public string? GetSearchScope(IntPtr hwnd)
        {
            return "__UniversalList__";
        }

        public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
        {
            if (hwnd == IntPtr.Zero) return false;

            IntPtr targetHwnd = hwnd;
            var sbClass = new StringBuilder(256);
            Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();

            if (!className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase) &&
                !className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr focusedCtrl = ListControlHelper.GetFocusedControl(hwnd);
                if (focusedCtrl != IntPtr.Zero)
                {
                    targetHwnd = focusedCtrl;
                    sbClass.Clear();
                    Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
                    className = sbClass.ToString();
                }
            }

            var items = ListControlHelper.GetListItemsInternal(targetHwnd, className);
            int matchedIndex = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    matchedIndex = i;
                    break;
                }
            }

            if (matchedIndex != -1)
            {
                ListControlHelper.SelectItem(targetHwnd, className, matchedIndex, clearOthers: false, selectState: true);
                _originallySelectedIndices.Add(matchedIndex);
                _lastPreviewIndex = -1;
                ListControlHelper.PostEnterKey(targetHwnd);
                return true;
            }

            return false;
        }

        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
        {
            rect = default;
            if (hwnd == IntPtr.Zero) return false;

            IntPtr targetHwnd = hwnd;
            var sbClass = new StringBuilder(256);
            Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();

            if (!className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase) &&
                !className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr focusedCtrl = ListControlHelper.GetFocusedControl(hwnd);
                if (focusedCtrl != IntPtr.Zero)
                {
                    targetHwnd = focusedCtrl;
                }
            }

            var nativeRect = new Win32Api.RECT();
            if (Win32Api.GetWindowRect(targetHwnd, out nativeRect))
            {
                rect = new AdapterRect 
                { 
                    Left = nativeRect.Left, 
                    Top = nativeRect.Top, 
                    Right = nativeRect.Right, 
                    Bottom = nativeRect.Bottom 
                };
                return true;
            }
            return false;
        }

        public bool CanEnterActionsMode(IntPtr hwnd)
        {
            return false;
        }

        public IEnumerable<string> GetListItems(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return Array.Empty<string>();

            IntPtr targetHwnd = hwnd;
            var sbClass = new StringBuilder(256);
            Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();

            if (!className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase) &&
                !className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr focusedCtrl = ListControlHelper.GetFocusedControl(hwnd);
                if (focusedCtrl != IntPtr.Zero)
                {
                    targetHwnd = focusedCtrl;
                    sbClass.Clear();
                    Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
                    className = sbClass.ToString();
                }
            }

            _originallySelectedIndices.Clear();
            _lastHwnd = targetHwnd;
            _lastPreviewIndex = -1;
            try
            {
                var sel = ListControlHelper.GetSelectedIndices(targetHwnd, className);
                foreach (var idx in sel)
                {
                    _originallySelectedIndices.Add(idx);
                }
            }
            catch { }

            var items = ListControlHelper.GetListItemsInternal(targetHwnd, className);
            return items;
        }

        public void OnSelectionChanged(IntPtr hwnd, string path)
        {
            if (hwnd == IntPtr.Zero) return;

            IntPtr targetHwnd = hwnd;
            var sbClass = new StringBuilder(256);
            Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();

            if (!className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase) &&
                !className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr focusedCtrl = ListControlHelper.GetFocusedControl(hwnd);
                if (focusedCtrl != IntPtr.Zero)
                {
                    targetHwnd = focusedCtrl;
                    sbClass.Clear();
                    Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
                    className = sbClass.ToString();
                }
            }

            var items = ListControlHelper.GetListItemsInternal(targetHwnd, className);
            int matchedIndex = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    matchedIndex = i;
                    break;
                }
            }

            if (matchedIndex != -1)
            {
                bool isMulti = ListControlHelper.IsMultiSelect(targetHwnd, className);
                if (isMulti)
                {
                    if (_lastPreviewIndex != -1 && _lastPreviewIndex != matchedIndex && !_originallySelectedIndices.Contains(_lastPreviewIndex))
                    {
                        ListControlHelper.SelectItem(targetHwnd, className, _lastPreviewIndex, clearOthers: false, selectState: false);
                    }
                    ListControlHelper.SelectItem(targetHwnd, className, matchedIndex, clearOthers: false, selectState: true);
                    _lastPreviewIndex = matchedIndex;
                }
                else
                {
                    ListControlHelper.SelectItem(targetHwnd, className, matchedIndex, clearOthers: true, selectState: true);
                }
            }
        }

        public void OnSearchFinished(IntPtr hwnd, bool executed)
        {
            if (hwnd == IntPtr.Zero) return;

            IntPtr targetHwnd = hwnd;
            var sbClass = new StringBuilder(256);
            Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();

            if (!className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase) &&
                !className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr focusedCtrl = ListControlHelper.GetFocusedControl(hwnd);
                if (focusedCtrl != IntPtr.Zero)
                {
                    targetHwnd = focusedCtrl;
                    sbClass.Clear();
                    Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
                    className = sbClass.ToString();
                }
            }

            if (!executed)
            {
                bool isMulti = ListControlHelper.IsMultiSelect(targetHwnd, className);
                if (isMulti)
                {
                    if (className.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
                    {
                        Win32Api.SendMessage(targetHwnd, Win32Api.LB_SETSEL, IntPtr.Zero, (IntPtr)(-1));
                    }
                    else if (className.Contains("SysListView32", StringComparison.OrdinalIgnoreCase))
                    {
                        Win32Api.GetWindowThreadProcessId(targetHwnd, out uint pid);
                        IntPtr hProcess = Win32Api.OpenProcess(Win32Api.PROCESS_VM_OPERATION | Win32Api.PROCESS_VM_WRITE, false, pid);
                        if (hProcess != IntPtr.Zero)
                        {
                            uint lvItemSize = (uint)Marshal.SizeOf<Win32Api.LVITEM>();
                            IntPtr remoteLvItemPtr = Win32Api.VirtualAllocEx(hProcess, IntPtr.Zero, lvItemSize, Win32Api.MEM_COMMIT, Win32Api.PAGE_READWRITE);
                            if (remoteLvItemPtr != IntPtr.Zero)
                            {
                                var clearAllItem = new Win32Api.LVITEM
                                {
                                    state = 0,
                                    stateMask = Win32Api.LVIS_SELECTED | Win32Api.LVIS_FOCUSED
                                };
                                Win32Api.WriteProcessMemory(hProcess, remoteLvItemPtr, ref clearAllItem, lvItemSize, out _);
                                Win32Api.SendMessage(targetHwnd, Win32Api.LVM_SETITEMSTATE, (IntPtr)(-1), remoteLvItemPtr);
                                Win32Api.VirtualFreeEx(hProcess, remoteLvItemPtr, 0, Win32Api.MEM_RELEASE);
                            }
                            Win32Api.CloseHandle(hProcess);
                        }
                    }

                    foreach (int idx in _originallySelectedIndices)
                    {
                        ListControlHelper.SelectItem(targetHwnd, className, idx, clearOthers: false, selectState: true);
                    }
                }
                else
                {
                    if (_originallySelectedIndices.Count > 0)
                    {
                        foreach (int idx in _originallySelectedIndices)
                        {
                            ListControlHelper.SelectItem(targetHwnd, className, idx, clearOthers: true, selectState: true);
                            break;
                        }
                    }
                }
            }

            _originallySelectedIndices.Clear();
            _lastPreviewIndex = -1;
            _lastHwnd = IntPtr.Zero;
        }
    }
}
