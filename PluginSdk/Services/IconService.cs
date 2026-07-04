using System.Windows.Media;

namespace SwiftList.PluginSdk.Services;

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

    /// <summary>
    /// Delegate registered by the main application to fetch a large real thumbnail (video frame, document
    /// page, image) for the given path at the requested pixel size, or null if unavailable.
    /// </summary>
    public static Func<string, int, ImageSource?> GetThumbnailFunc { get; set; } = (path, size) => null;

    /// <summary>
    /// Retrieves a large thumbnail for the path (uncached), or null when the shell has none.
    /// </summary>
    public static ImageSource? GetThumbnail(string path, int size) => GetThumbnailFunc(path, size);
}
