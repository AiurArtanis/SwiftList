using System.Text;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;
using SwiftList.Plugins.ListSearch.Helpers;

namespace SwiftList.Plugins.ListSearch;

public class ListSearchInlineSearchAdapter : IInlineSearchAdapter
{
    private const string UniversalListScope = "__UniversalList__";
    public string Name => TranslationService.Get("Plugins_ListSearchTargetName");

    public string Description => TranslationService.Get("Plugin_Comp_Desc_ListSearchInlineSearchAdapter");

    private readonly HashSet<int> _originallySelectedIndices = new HashSet<int>();
    private IntPtr _lastHwnd = IntPtr.Zero;
    private int _lastPreviewIndex = -1;
    private List<string>? _cachedItems;

    private (IntPtr targetHwnd, string className) ResolveTargetControl(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return (IntPtr.Zero, string.Empty);
        var targetHwnd = hwnd;
        var sbClass = new StringBuilder(256);
        Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
        var className = sbClass.ToString();

        if (!ListSearchIndexEncoder.IsListViewClass(className) &&
            !ListSearchIndexEncoder.IsListBoxClass(className))
        {
            var focusedCtrl = ListControlHelper.GetFocusedControl(hwnd);
            if (focusedCtrl != IntPtr.Zero)
            {
                targetHwnd = focusedCtrl;
                sbClass.Clear();
                Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
                className = sbClass.ToString();
            }
        }
        return (targetHwnd, className);
    }

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (string.IsNullOrEmpty(className)) return false;
        if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return false;

        return ListSearchIndexEncoder.IsListViewClass(className) ||
               ListSearchIndexEncoder.IsListBoxClass(className);
    }

    public bool CanTrigger(IntPtr focusedHwnd, string className)
    {
        if (focusedHwnd == IntPtr.Zero || string.IsNullOrEmpty(className))
            return false;

        return ListSearchIndexEncoder.IsListViewClass(className) ||
               ListSearchIndexEncoder.IsListBoxClass(className);
    }

    public string? GetSearchScope(IntPtr hwnd) => UniversalListScope;

    public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(path, UniversalListScope, StringComparison.Ordinal))
            return false;

        var (targetHwnd, className) = ResolveTargetControl(hwnd);
        if (targetHwnd == IntPtr.Zero) return false;

        var matchedIndex = ListSearchIndexEncoder.DecodeIndex(path);
        if (matchedIndex != -1)
        {
            var isMulti = ListControlHelper.IsMultiSelect(targetHwnd, className);
            if (isMulti)
            {
                var wasSelected = _originallySelectedIndices.Contains(matchedIndex);
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
        var (targetHwnd, _) = ResolveTargetControl(hwnd);
        if (targetHwnd == IntPtr.Zero) return false;

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
        var (targetHwnd, className) = ResolveTargetControl(hwnd);
        if (targetHwnd == IntPtr.Zero) return Array.Empty<string>();

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

        return GetListItemsLazy(targetHwnd, className);
    }

    private IEnumerable<string> GetListItemsLazy(IntPtr targetHwnd, string className)
    {
        var items = ListControlHelper.GetListItemsInternal(targetHwnd, className);
        var resultList = new List<string>();
        var i = 0;
        foreach (var item in items)
        {
            var encoded = ListSearchIndexEncoder.EncodeIndex(item, i);
            resultList.Add(encoded);
            i++;
            yield return encoded;
        }
        _cachedItems = resultList;
    }

    public void OnSelectionChanged(IntPtr hwnd, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(path, UniversalListScope, StringComparison.Ordinal))
            return;

        var (targetHwnd, className) = ResolveTargetControl(hwnd);
        if (targetHwnd == IntPtr.Zero) return;

        var matchedIndex = ListSearchIndexEncoder.DecodeIndex(path);
        if (matchedIndex != -1)
        {
            var isMulti = ListControlHelper.IsMultiSelect(targetHwnd, className);
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
        var (targetHwnd, className) = ResolveTargetControl(hwnd);
        if (targetHwnd == IntPtr.Zero) return;

        if (!executed)
        {
            var isMulti = ListControlHelper.IsMultiSelect(targetHwnd, className);
            if (isMulti)
            {
                ListControlHelper.ClearSelection(targetHwnd, className);
                foreach (var idx in _originallySelectedIndices)
                {
                    ListControlHelper.SelectItem(targetHwnd, className, idx, clearOthers: false, selectState: true);
                }
            }
            else
            {
                if (_originallySelectedIndices.Count > 0)
                {
                    foreach (var idx in _originallySelectedIndices)
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
}
