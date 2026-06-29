using System.Text;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.Plugins.FolderCascader.Navigation;

public class Provider : IQuickNavigationProvider
{
    private readonly Dictionary<IntPtr, string> _nodeMap = new();
    private readonly Dictionary<uint, string> _commandMap = new();
    private int _nextId = 1;
    private uint _nextCmdId = 1;

    public bool CanShow(IntPtr activeHwnd, string processName, string className, bool isDesktop, int x, int y, MouseTriggerType triggerType)
    {
        if (!string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase) && !isDesktop)
        {
            return false;
        }

        var hwndUnderCursor = Win32Native.WindowFromPoint(new Win32Native.POINT(x, y));
        if (hwndUnderCursor == IntPtr.Zero) return false;

        if (!Win32Native.IsDescendantOfShellDllDefView(hwndUnderCursor)) return false;

        var sbClass = new StringBuilder(256);
        Win32Native.GetClassName(hwndUnderCursor, sbClass, sbClass.Capacity);
        var clsName = sbClass.ToString();

        if (!clsName.Equals("DirectUIHWND", StringComparison.OrdinalIgnoreCase) &&
            !clsName.Equals("SysListView32", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (clsName.Equals("SysListView32", StringComparison.OrdinalIgnoreCase))
        {
            // ponytail: Check if user clicked on a desktop icon using cross-process LVM_HITTEST.
            // Ceiling: Fallback to Shell selectedItems count if process open fails or memory allocation fails.
            if (Win32Native.IsPointOnDesktopIcon(hwndUnderCursor, x, y))
            {
                return false;
            }
        }

        if (!Win32Native.IsActiveWindowFolderEmptySpace(activeHwnd))
        {
            return false;
        }

        return true;
    }

    public bool CanProvide(ISearchResult result) => result != null && !string.IsNullOrEmpty(result.FullPath);

    public IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu) =>
        MenuBuilder.GetMenuItems(result, hMenu, this);

    public void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd)
    {
        if (_commandMap.TryGetValue(commandId, out var path))
            CommandExecutor.Execute(result, path);
    }

    public void ClearSession()
    {
        _nodeMap.Clear();
        _commandMap.Clear();
        _nextId = 1;
        _nextCmdId = 1;
    }

    public IntPtr AllocateHandle(string path)
    {
        var handle = new IntPtr(_nextId++);
        _nodeMap[handle] = path;
        return handle;
    }

    public uint AllocateCommand(string path)
    {
        var cmdId = _nextCmdId++;
        _commandMap[cmdId] = path;
        return cmdId;
    }

    public bool TryGetPath(IntPtr handle, out string? path) => _nodeMap.TryGetValue(handle, out path);
}
