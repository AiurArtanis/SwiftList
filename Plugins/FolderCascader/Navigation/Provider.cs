using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.Plugins.FolderCascader.Navigation;

// Content-only: deciding whether the Quick Navigation popup should open at all for a given click lives
// in App/Services/ShellMenu/QuickNavigationTriggerGate.cs, not here -- that's host recognition (Explorer
// empty-space hit-testing, other file managers via their adapters), not something specific to what this
// class contributes to the popup once it's already open.
public class Provider : IQuickNavigationProvider
{
    private readonly Dictionary<IntPtr, string> _nodeMap = new();
    private readonly Dictionary<uint, string> _commandMap = new();
    private int _nextId = 1;
    private uint _nextCmdId = 1;

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
