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
        private List<string>? _cachedItems;

        private static bool IsListBoxClass(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            return className.Equals("ListBox", StringComparison.OrdinalIgnoreCase) ||
                   className.Contains(".ListBox.", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsListViewClass(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            return className.Equals("SysListView32", StringComparison.OrdinalIgnoreCase) ||
                   className.Contains(".SysListView32.", StringComparison.OrdinalIgnoreCase);
        }

        public bool CanHandle(IntPtr hwnd, string className, string processName)
        {
            if (string.IsNullOrEmpty(className)) return false;
            if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return false;

            return IsListViewClass(className) ||
                   IsListBoxClass(className);
        }

        public bool CanTrigger(IntPtr focusedHwnd, string className)
        {
            if (focusedHwnd == IntPtr.Zero || string.IsNullOrEmpty(className))
                return false;

            return IsListViewClass(className) ||
                   IsListBoxClass(className);
        }

        public string? GetSearchScope(IntPtr hwnd) => "__UniversalList__";

        public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
        {
            if (hwnd == IntPtr.Zero) return false;

            IntPtr targetHwnd = hwnd;
            var sbClass = new StringBuilder(256);
            Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();

            if (!IsListViewClass(className) &&
                !IsListBoxClass(className))
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

            int matchedIndex = DecodeIndex(path);

            if (matchedIndex != -1)
            {
                bool isMulti = ListControlHelper.IsMultiSelect(targetHwnd, className);
                if (isMulti)
                {
                    // Toggle: if the item was originally selected, deselect it; otherwise select it.
                    bool wasSelected = _originallySelectedIndices.Contains(matchedIndex);
                    ListControlHelper.SelectItem(targetHwnd, className, matchedIndex,
                        clearOthers: false,
                        selectState: !wasSelected);
                    if (wasSelected)
                        _originallySelectedIndices.Remove(matchedIndex);
                    else
                        _originallySelectedIndices.Add(matchedIndex);
                }
                else
                {
                    ListControlHelper.SelectItem(targetHwnd, className, matchedIndex,
                        clearOthers: true,
                        selectState: true);
                    _originallySelectedIndices.Clear();
                    _originallySelectedIndices.Add(matchedIndex);
                }

                _lastPreviewIndex = -1;
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

            if (!IsListViewClass(className) &&
                !IsListBoxClass(className))
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

        public bool CanEnterActionsMode(IntPtr hwnd) => false;

        public IEnumerable<string> GetListItems(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return Array.Empty<string>();

            IntPtr targetHwnd = hwnd;
            var sbClass = new StringBuilder(256);
            Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();

            if (!IsListViewClass(className) &&
                !IsListBoxClass(className))
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

            if (_cachedItems != null && targetHwnd == _lastHwnd)
            {
                return _cachedItems;
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
            var result = new List<string>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                // Encode the index into a very short zero-width binary suffix to avoid heavy string allocations and WPF layout lag
                var sbSuffix = new StringBuilder();
                sbSuffix.Append('\u200D'); // Start marker
                string binary = Convert.ToString(i, 2);
                foreach (char bit in binary)
                {
                    sbSuffix.Append(bit == '1' ? '\u200C' : '\u200B');
                }
                result.Add(items[i] + sbSuffix.ToString());
            }
            _cachedItems = result;
            return result;
        }

        public void OnSelectionChanged(IntPtr hwnd, string path)
        {
            if (hwnd == IntPtr.Zero) return;

            IntPtr targetHwnd = hwnd;
            var sbClass = new StringBuilder(256);
            Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();

            if (!IsListViewClass(className) &&
                !IsListBoxClass(className))
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

            int matchedIndex = DecodeIndex(path);

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

            if (!IsListViewClass(className) &&
                !IsListBoxClass(className))
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
                    ListControlHelper.ClearSelection(targetHwnd, className);
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
            _cachedItems = null;
        }

        private int DecodeIndex(string path)
        {
            if (string.IsNullOrEmpty(path)) return -1;
            int markerIndex = path.LastIndexOf('\u200D');
            if (markerIndex == -1 || markerIndex == path.Length - 1) return -1;

            var sb = new StringBuilder();
            for (int i = markerIndex + 1; i < path.Length; i++)
            {
                char c = path[i];
                if (c == '\u200C') sb.Append('1');
                else if (c == '\u200B') sb.Append('0');
                else break;
            }
            try
            {
                return Convert.ToInt32(sb.ToString(), 2);
            }
            catch
            {
                return -1;
            }
        }
    }
}
