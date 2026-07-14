namespace SwiftList.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Plugin interface to register custom filter categories and items in the Search Window sidebar.
/// </summary>
public interface ISidebarFilterProvider : IPluginComponent
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
    /// Batch predicate to narrow a result set down to what matches this filter. Takes the full
    /// list at once (not one item at a time) so an implementation that needs metadata (size,
    /// dates) can fetch it for every item in a single batched call instead of one lookup per item.
    /// </summary>
    public Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>>? FilterPredicate { get; set; }
}
