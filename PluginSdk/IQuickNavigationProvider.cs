namespace SwiftList.PluginSdk;

/// <summary>
/// Defines a provider that supplies items for the Quick Navigation menu.
/// </summary>
public interface IQuickNavigationProvider
{
    /// <summary>
    /// Determines whether this provider can supply navigation items for the given search result.
    /// </summary>
    bool CanProvide(ISearchResult result);

    /// <summary>
    /// Gets menu items for the navigation node or submenu handle.
    /// </summary>
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);

    /// <summary>
    /// Executes the command associated with the command ID.
    /// </summary>
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);

    /// <summary>
    /// Determines whether the quick navigation menu can be shown for the specified active window and process.
    /// </summary>
    bool CanShow(IntPtr activeHwnd, string processName, string className, bool isDesktop, int x, int y);

    /// <summary>
    /// Clears any cached handles or states.
    /// </summary>
    void ClearSession();
}
