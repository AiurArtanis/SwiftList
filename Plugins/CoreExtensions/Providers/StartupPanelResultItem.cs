using System.IO;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Helpers;

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
        // A packaged app's path is a virtual shell:AppsFolder\{AUMID} id, not a real filename --
        // Path.GetFileName on it would surface the raw AUMID, so resolve the shell's own friendly
        // display name instead (same fallback Favorites already uses for shell:/:: paths).
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("::", StringComparison.Ordinal))
            return ShellPathHelper.GetVirtualFolderDisplayName(path, path);

        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
