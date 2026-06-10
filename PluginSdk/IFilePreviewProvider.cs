using System.Windows;

namespace SwiftList.PluginSdk;

/// <summary>
/// Interface that allows plugins to provide custom UI previews for files or folders.
/// </summary>
public interface IFilePreviewProvider
{
    /// <summary>Gets the name of this preview provider.</summary>
    string Name { get; }

    /// <summary>Gets the priority of this provider (higher runs first).</summary>
    int Priority => 0;

    /// <summary>
    /// Determines if this provider can build a preview for the given path.
    /// </summary>
    bool CanPreview(string path, bool isDir);

    /// <summary>
    /// Creates a WPF UI control displaying the preview of the path.
    /// </summary>
    UIElement CreatePreview(string path, bool isDir);
}
