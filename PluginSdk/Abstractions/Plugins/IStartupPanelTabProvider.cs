namespace SwiftList.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Contributes a tab to the quick window's Startup Panel, shown above the result list
/// when the search box is empty. The host renders whatever items are returned through its own result
/// list (icons, open, actions menu all come for free) -- a provider only needs to say what to show
/// right now. A tab whose items are empty is hidden automatically; there's nothing else to configure.
/// </summary>
public interface IStartupPanelTabProvider : IPluginComponent
{

    /// <summary>
    /// Returns the items to show right now. Called synchronously each time the panel is (re)activated --
    /// keep this fast (no disk/network I/O beyond what the host process already has in memory).
    /// </summary>
    IEnumerable<ISearchResult> GetItems();
}
