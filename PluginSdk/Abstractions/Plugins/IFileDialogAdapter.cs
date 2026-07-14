namespace SwiftList.PluginSdk.Abstractions.Plugins;

public interface IFileDialogAdapter : IPluginComponent
{

    /// <summary>
    /// Check if this adapter can handle the given active window.
    /// </summary>
    bool CanHandle(IntPtr hwnd, string className, string processName);

    /// <summary>
    /// Retrieves the current folder path from the active dialog window.
    /// </summary>
    string? GetCurrentPath(IntPtr hwnd);

    /// <summary>
    /// Navigates the dialog window to the target directory.
    /// </summary>
    bool NavigateTo(IntPtr hwnd, string targetPath);

    /// <summary>
    /// Whether Quick Navigation should trigger for a middle-click at the given point inside this dialog.
    /// Default true once <see cref="CanHandle"/> has already matched the dialog itself: unlike a full
    /// file-manager window, a common dialog has no "click a toolbar/breadcrumb button" action for a
    /// middle-click to collide with, so no extra child-control probe is needed unless a specific adapter
    /// wants one -- mirrors <see cref="IInlineSearchAdapter.CanShowQuickNav"/>'s same default-to-CanTrigger
    /// reasoning.
    /// </summary>
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;

    /// <summary>
    /// Gets the window bounds for docking the inline search window.
    /// </summary>
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);

    /// <summary>
    /// Restores focus to the appropriate control in the dialog window.
    /// </summary>
    bool RestoreFocus(IntPtr hwnd);
}

public struct AdapterRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
