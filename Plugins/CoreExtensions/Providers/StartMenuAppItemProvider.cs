using System.IO;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Helpers;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Providers;

/// <summary>
/// Searchable item provider that scans all start menu and desktop folders
/// and indexes applications/shortcuts as first-class searchable items.
/// </summary>
public class StartMenuAppItemProvider : ISearchableItemProvider, IDisposable
{
    public string Name => TranslationService.Get("Plugins_StartMenuAppItemProviderName") ?? "Start Menu Applications";

    public event Action? ItemsChanged;

    public StartMenuAppItemProvider()
    {
        DirectoryIndexerService.DirectoryChanged += OnDirectoryChanged;
        try
        {
            foreach (var root in StartMenuShortcutResolver.GetStartMenuRoots())
            {
                if (!Directory.Exists(root))
                    continue;

                // Register directory to the host system indexer for global monitoring and search
                DirectoryIndexerService.RegisterDirectory("CoreExtensions.StartMenu", root, recursive: true, filterPattern: "*.lnk");
            }
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[StartMenuAppItemProvider] Failed to register directories to indexer: {ex.Message}", PluginSdk.LogLevel.Warn);
        }
    }

    private void OnDirectoryChanged(string pluginId)
    {
        if (string.Equals(pluginId, "CoreExtensions.StartMenu", StringComparison.OrdinalIgnoreCase))
        {
            ItemsChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        DirectoryIndexerService.DirectoryChanged -= OnDirectoryChanged;
        try
        {
            DirectoryIndexerService.UnregisterDirectories("CoreExtensions.StartMenu");
        }
        catch { }
        GC.SuppressFinalize(this);
    }

    public IEnumerable<SearchableItem> GetSearchableItems()
    {
        var list = new List<SearchableItem>();
        var indexedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entriesByName = new Dictionary<string, List<(string Name, string Path)>>(StringComparer.OrdinalIgnoreCase);

        // 1. Collect scan roots: built-in Start Menu/Desktop + user-configured custom folders
        var roots = StartMenuShortcutResolver.GetStartMenuRoots().ToList();
        try
        {
            var customFolders = PluginSettingsService.GetSetting<List<string>>("SwiftList.Plugins.CoreExtensions", "CustomFolders", null!);
            if (customFolders != null)
            {
                foreach (var p in customFolders)
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    var expanded = Environment.ExpandEnvironmentVariables(p.Trim());
                    if (Directory.Exists(expanded)) roots.Add(expanded);
                }
            }
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[StartMenuAppItemProvider] Failed to load custom folders config: {ex.Message}", PluginSdk.LogLevel.Warn);
        }

        // 2. Gather all unique shortcut files from all roots
        foreach (var root in roots)
        {
            foreach (var path in StartMenuShortcutResolver.EnumerateFilesSafe(root))
            {
                if (!StartMenuShortcutResolver.ShouldIndex(path) || !indexedPaths.Add(path))
                    continue;

                var name = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!entriesByName.TryGetValue(name, out var entries))
                {
                    entries = new List<(string Name, string Path)>();
                    entriesByName[name] = entries;
                }
                entries.Add((name, path));
            }
        }

        // 3. Deduplicate entries that have the same name by target executable path
        var deduped = new List<(string Name, string Path)>();
        foreach (var group in entriesByName.Values)
        {
            if (group.Count == 1)
            {
                deduped.Add(group[0]);
                continue;
            }

            var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in group)
            {
                var target = StartMenuShortcutResolver.ResolveShortcutTarget(entry.Path) ?? entry.Path;
                if (seenTargets.Add(target))
                {
                    deduped.Add(entry);
                }
            }
        }

        // 4. Map to SearchableItem list with dynamic icon loading
        var descTemplate = TranslationService.Get("Search_ResultAppDir") ?? "Application · {0}";
        foreach (var entry in deduped)
        {
            var capturedPath = entry.Path;
            var targetPath = StartMenuShortcutResolver.ResolveShortcutTarget(capturedPath) ?? capturedPath;
            var parentDir = Path.GetDirectoryName(targetPath);
            var desc = string.IsNullOrWhiteSpace(parentDir)
                ? (TranslationService.Get("Search_ResultApp") ?? "Application")
                : string.Format(descTemplate, parentDir);

            var hBitmap = ShellPathHelper.GetIconHBitmapForPath(targetPath, 96);
            if (hBitmap == IntPtr.Zero && targetPath != capturedPath)
            {
                hBitmap = ShellPathHelper.GetIconHBitmapForPath(capturedPath, 96);
            }

            list.Add(new SearchableItem
            {
                Title = entry.Name,
                Description = desc,
                ResultKind = "Application",
                HBitmapIcon = hBitmap,
                ActionType = "None",
                ActionArgument = capturedPath,
                OnExecute = () =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = capturedPath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        PluginSdk.Logger.Log($"[StartMenuAppItemProvider] Failed to launch '{entry.Name}': {ex.Message}", PluginSdk.LogLevel.Error);
                    }
                }
            });
        }

        return list;
    }
}
