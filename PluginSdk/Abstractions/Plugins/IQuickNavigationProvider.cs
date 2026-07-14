namespace SwiftList.PluginSdk.Abstractions.Plugins;

public enum MouseTriggerType
{
    DoubleClick,
    MiddleClick
}

/// <summary>
/// Defines a provider that supplies items for the Quick Navigation menu. Purely a content source --
/// whether the popup should open at all for a given click is a separate concern, decided by
/// <see cref="IQuickNavigationTriggerGate"/> (or, for file dialogs, <see cref="IFileDialogAdapter.CanShowQuickNav"/>).
/// Most plugins only need to implement this interface.
/// </summary>
public interface IQuickNavigationProvider : IPluginComponent
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
    /// Clears any cached handles or states.
    /// </summary>
    void ClearSession();
}
