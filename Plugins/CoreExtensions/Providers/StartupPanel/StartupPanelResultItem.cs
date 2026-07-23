using System.IO;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Helpers;

namespace SwiftList.Plugins.CoreExtensions.Providers.StartupPanel;

// Minimal ISearchResult implementation shared by the startup-panel tab providers below -- these just
// need to hand the host a path (plus an optional user-given display name for favorites).
internal sealed class StartupPanelResultItem : ISearchResult
{
    public string Name { get; }
    public string FullPath { get; }
    public string ContextDirectory { get; }
    public bool IsDir { get; }
    public bool IsApplication { get; }

    public StartupPanelResultItem(string path, string? displayName = null, bool isApplication = false)
    {
        FullPath = path;
        IsApplication = isApplication;
        IsDir = !isApplication && Directory.Exists(path);
        Name = string.IsNullOrWhiteSpace(displayName) ? DeriveName(path, isApplication) : displayName!;
        ContextDirectory = IsDir ? path : (Path.GetDirectoryName(path) ?? path);
    }

    private static string DeriveName(string path, bool isApplication)
    {
        // A packaged app's path is a virtual shell:AppsFolder\{AUMID} id, not a real filename --
        // Path.GetFileName on it would surface the raw AUMID, so resolve the shell's own friendly
        // display name instead (same fallback Favorites already uses for shell:/:: paths).
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("::", StringComparison.Ordinal))
            return ShellPathHelper.GetVirtualFolderDisplayName(path, path);

        var name = Path.GetFileName(path.TrimEnd('\\', '/'));

        // A classic Start Menu app's FullPath is its .lnk shortcut file itself (see
        // StartMenuAppItemProvider), whose own display name is the filename WITHOUT that extension
        // (Path.GetFileNameWithoutExtension) -- matches what the main results list shows for the same
        // app, instead of leaking the raw ".lnk" suffix into the name here.
        if (isApplication && name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);

        return string.IsNullOrEmpty(name) ? path : name;
    }
}
