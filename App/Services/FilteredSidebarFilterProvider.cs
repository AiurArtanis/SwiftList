using SwiftList.PluginSdk;
using SwiftList.App.ViewModels.Settings.Plugins;

namespace SwiftList.App.Services;

public class FilteredSidebarFilterProvider : ISidebarFilterProvider
{
    private readonly ISidebarFilterProvider _inner;
    private readonly string _dllName;
    private readonly PluginManager _manager;

    public FilteredSidebarFilterProvider(ISidebarFilterProvider inner, string dllName, PluginManager manager)
    {
        _inner = inner;
        _dllName = dllName;
        _manager = manager;
    }

    public int SortOrder => _inner.SortOrder;

    public IEnumerable<SidebarFilterGroup> GetFilterGroups()
    {
        var index = 0;
        foreach (var group in _inner.GetFilterGroups())
        {
            if (_manager.IsComponentEnabled(_dllName, PluginComponentType.FilterProvider, $"{_inner.GetType().Name}_{index}"))
            {
                yield return group;
            }
            index++;
        }
    }
}
