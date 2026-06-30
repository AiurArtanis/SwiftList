using System.Windows.Media;

namespace SwiftList.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Provides custom file thumbnails or icon overrides for search results.
/// </summary>
public interface IThumbnailProvider
{
    /// <summary>
    /// Gets the unique identifier for the provider.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the display name for the provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Checks if this provider can generate a thumbnail/icon for the specified file path and directory state.
    /// </summary>
    bool CanProvideThumbnail(string path, bool isDir);

    /// <summary>
    /// Generates or retrieves the thumbnail for the specified file path.
    /// </summary>
    ImageSource? GetThumbnail(string path, int size);
}
