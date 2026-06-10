namespace SwiftList.PluginSdk;

/// <summary>
/// Plugin interface to register custom filter categories and items in the Search Window sidebar.
/// </summary>
public interface ISidebarFilterProvider
{
    /// <summary>
    /// Returns the filter groups to be displayed in the sidebar.
    /// </summary>
    IEnumerable<SidebarFilterGroup> GetFilterGroups();

    /// <summary>
    /// Ordering weight. Lower values render first.
    /// </summary>
    int SortOrder => 100;
}

/// <summary>
/// Represents a group of sidebar filter items.
/// </summary>
public class SidebarFilterGroup
{
    public string Header { get; set; } = string.Empty;
    public List<SidebarFilterItem> Items { get; set; } = new();
}

/// <summary>
/// Represents a filter item in a sidebar group.
/// </summary>
public class SidebarFilterItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Vector icon path geometry (optional).
    /// </summary>
    public string? IconData { get; set; }

    /// <summary>
    /// Key used for UI icon matching if vector path is not supplied (optional).
    /// </summary>
    public string? IconKey { get; set; }

    /// <summary>
    /// Predicate to execute on a search result to check if it matches this filter.
    /// </summary>
    public Func<ISearchResult, bool>? FilterPredicate { get; set; }
}
