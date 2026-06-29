namespace SwiftList.PluginSdk.Abstractions.Plugins;

public interface IFileDialogAdapter
{
    /// <summary>
    /// Name of the adapter (e.g., "Standard Windows File Dialog", "Qt File Dialog")
    /// </summary>
    string Name { get; }

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
