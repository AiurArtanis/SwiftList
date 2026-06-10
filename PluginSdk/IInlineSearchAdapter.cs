namespace SwiftList.PluginSdk;

public interface IInlineSearchAdapter
{
    /// <summary>
    /// Name of the inline search adapter (e.g., "Notepad Inline Search Adapter").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Check if this adapter can handle the given active window.
    /// </summary>
    bool CanHandle(IntPtr hwnd, string className, string processName);

    /// <summary>
    /// Check if the inline search window can be triggered when the specified control has focus.
    /// </summary>
    bool CanTrigger(IntPtr focusedHwnd, string className);

    /// <summary>
    /// Retrieves the current search scope path from the active window.
    /// </summary>
    string? GetSearchScope(IntPtr hwnd);

    /// <summary>
    /// Defines the action when a search result item is clicked or executed.
    /// Returns true if the action was successfully handled, false otherwise.
    /// </summary>
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);

    /// <summary>
    /// Gets the window bounds for positioning/docking the inline search window.
    /// </summary>
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);

    /// <summary>
    /// Check if the secondary actions menu can be entered for the active window.
    /// </summary>
    bool CanEnterActionsMode(IntPtr hwnd);

    /// <summary>
    /// Returns the full list of items currently available in the target control.
    /// When non-empty, the inline search filters this list instead of querying the
    /// global search service. Return an empty enumerable (default) to fall back to
    /// the normal streaming search.
    /// </summary>
    IEnumerable<string> GetListItems(IntPtr hwnd) => Array.Empty<string>();

    /// <summary>
    /// Called when the highlighted result in the inline search window changes,
    /// so the adapter can mirror the selection in the host control in real time.
    /// </summary>
    void OnSelectionChanged(IntPtr hwnd, string path) { }

    /// <summary>
    /// Called when the search session ends.
    /// <paramref name="executed"/> is true when the user confirmed a result,
    /// false when the session was cancelled or the window was closed.
    /// </summary>
    void OnSearchFinished(IntPtr hwnd, bool executed) { }
}
