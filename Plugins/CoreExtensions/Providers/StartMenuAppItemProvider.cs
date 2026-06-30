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

    private readonly List<FileSystemWatcher> _watchers = new();

    public StartMenuAppItemProvider()
    {
        try
        {
            foreach (var root in StartMenuShortcutResolver.GetStartMenuRoots())
            {
                if (!Directory.Exists(root))
                    continue;

                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };

                FileSystemEventHandler handler = (s, e) => OnFileChanged();
                RenamedEventHandler renamedHandler = (s, e) => OnFileChanged();

                watcher.Created += handler;
                watcher.Deleted += handler;
                watcher.Changed += handler;
                watcher.Renamed += renamedHandler;

                _watchers.Add(watcher);
            }
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[StartMenuAppItemProvider] Failed to initialize file watchers: {ex.Message}", PluginSdk.LogLevel.Warn);
        }
    }

    private void OnFileChanged() => ItemsChanged?.Invoke();

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.Dispose();
            }
            catch { }
        }
        _watchers.Clear();
        GC.SuppressFinalize(this);
    }

    public IEnumerable<SearchableItem> GetSearchableItems()
    {
        var list = new List<SearchableItem>();
        var indexedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entriesByName = new Dictionary<string, List<(string Name, string Path)>>(StringComparer.OrdinalIgnoreCase);

        // 1. Gather all unique shortcut files from Start Menu and Desktop
        foreach (var root in StartMenuShortcutResolver.GetStartMenuRoots())
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

        // 2. Deduplicate entries that have the same name by target executable path
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

        // 3. Map to SearchableItem list with dynamic icon loading
        var descTemplate = TranslationService.Get("Search_ResultAppDir") ?? "Application · {0}";
        foreach (var entry in deduped)
        {
            var capturedPath = entry.Path;
            var targetPath = StartMenuShortcutResolver.ResolveShortcutTarget(capturedPath) ?? capturedPath;
            var parentDir = Path.GetDirectoryName(targetPath);
            var desc = string.IsNullOrWhiteSpace(parentDir)
                ? (TranslationService.Get("Search_ResultApp") ?? "Application")
                : string.Format(descTemplate, parentDir);

            // Fetch HBITMAP icon for this application (use targetPath to avoid shortcut arrow overlay)
            var hBitmap = ShellPathHelper.GetIconHBitmapForPath(targetPath, 32);
            if (hBitmap == IntPtr.Zero && targetPath != capturedPath)
            {
                hBitmap = ShellPathHelper.GetIconHBitmapForPath(capturedPath, 32);
            }

            list.Add(new SearchableItem
            {
                Title = entry.Name,
                Description = desc,
                ResultKind = "Application", // Mark as Application kind for UI rendering
                HBitmapIcon = hBitmap,
                ActionType = "None",
                ActionArgument = capturedPath,
                OnExecute = () =>
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = capturedPath,
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        PluginSdk.Logger.Log($"[StartMenuAppItemProvider] Failed to launch application '{entry.Name}': {ex.Message}", PluginSdk.LogLevel.Error);
                    }
                }
            });
        }

        return list;
    }
}
