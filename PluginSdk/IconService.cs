using System.Windows.Media;

namespace SwiftList.PluginSdk;

/// <summary>
/// Provides access to the main application's cached shell icon service.
/// </summary>
public static class IconService
{
    /// <summary>
    /// Delegate registered by the main application to fetch file/directory icons.
    /// </summary>
    public static Func<string, bool, ImageSource?> GetIconFunc { get; set; } = (path, isDir) => null;

    /// <summary>
    /// Retrieves the cached icon for the specified path.
    /// </summary>
    public static ImageSource? GetIcon(string path, bool isDir) => GetIconFunc(path, isDir);
}
