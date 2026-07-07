using System.IO;
using SwiftList.PluginSdk.Abstractions;

namespace SwiftList.Plugins.CoreExtensions.Providers;

// Minimal ISearchResult implementation shared by the startup-panel tab providers below -- these just
// need to hand the host a path (plus an optional user-given display name for favorites).
internal sealed class StartupPanelResultItem : ISearchResult
{
    public string Name { get; }
    public string FullPath { get; }
    public string ContextDirectory { get; }
    public bool IsDir { get; }
    public bool IsApplication => false;

    public StartupPanelResultItem(string path, string? displayName = null)
    {
        FullPath = path;
        IsDir = Directory.Exists(path);
        Name = string.IsNullOrWhiteSpace(displayName) ? DeriveName(path) : displayName!;
        ContextDirectory = IsDir ? path : (Path.GetDirectoryName(path) ?? path);
    }

    private static string DeriveName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
