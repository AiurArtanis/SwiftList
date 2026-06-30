using System.Collections.Concurrent;

namespace SwiftList.Core;

/// <summary>
/// Managed core indexer coordinator. Decides if a path should be query-routed
/// to the USN Service via NamedPipe or scanned locally (for network/removable drives).
/// </summary>
public sealed class CoreDirectoryIndexManager
{
    private static readonly Lazy<CoreDirectoryIndexManager> _instance = new(() => new CoreDirectoryIndexManager());
    public static CoreDirectoryIndexManager Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, List<MonitoredDir>> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly SearchService _searchService = new();
    private readonly ConcurrentDictionary<string, List<FileSystemWatcher>> _watchers = new(StringComparer.OrdinalIgnoreCase);

    private class MonitoredDir
    {
        public string Path { get; set; } = string.Empty;
        public bool Recursive { get; set; } = true;
        public string FilterPattern { get; set; } = "*";
    }

    private CoreDirectoryIndexManager()
    {
        // Bind the SDK delegates to this manager
        PluginSdk.Services.DirectoryIndexerService.RegisterDirectoryAction = RegisterDirectory;
        PluginSdk.Services.DirectoryIndexerService.UnregisterDirectoriesAction = UnregisterDirectories;
    }

    public void RegisterDirectory(string pluginId, string directoryPath, bool recursive, string filterPattern)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return;
        var fullPath = Path.GetFullPath(directoryPath);

        var list = _registrations.GetOrAdd(pluginId, _ => new List<MonitoredDir>());
        lock (list)
        {
            if (!list.Any(d => string.Equals(d.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new MonitoredDir
                {
                    Path = fullPath,
                    Recursive = recursive,
                    FilterPattern = filterPattern
                });
                Logger.Log($"[IndexManager] Plugin '{pluginId}' registered directory: '{fullPath}' (Recursive={recursive}, Filter={filterPattern})");

                // Set up FileSystemWatcher for monitoring changes and alerting the plugin via SDK event
                CreateWatcher(pluginId, fullPath, recursive, filterPattern);
            }
        }
    }

    private void CreateWatcher(string pluginId, string fullPath, bool recursive, string filterPattern)
    {
        if (!Directory.Exists(fullPath))
        {
            // If the folder is missing (disconnected drive), start reconnect loop
            _ = Task.Run(() => TryRecreateWatcherAsync(pluginId, fullPath, recursive, filterPattern));
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(fullPath)
            {
                IncludeSubdirectories = recursive,
                Filter = filterPattern,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            FileSystemEventHandler handler = (s, e) => PluginSdk.Services.DirectoryIndexerService.NotifyDirectoryChanged(pluginId);
            RenamedEventHandler renamedHandler = (s, e) => PluginSdk.Services.DirectoryIndexerService.NotifyDirectoryChanged(pluginId);

            watcher.Created += handler;
            watcher.Deleted += handler;
            watcher.Changed += handler;
            watcher.Renamed += renamedHandler;

            // Handle disconnection error by starting recovery loop
            watcher.Error += (s, e) =>
            {
                Logger.Log($"[IndexManager] Watcher error for '{fullPath}' (Plugin: {pluginId}): {e.GetException().Message}. Retrying...", LogLevel.Warn);
                RemoveWatcher(pluginId, watcher);
                _ = Task.Run(() => TryRecreateWatcherAsync(pluginId, fullPath, recursive, filterPattern));
            };

            var watcherList = _watchers.GetOrAdd(pluginId, _ => new List<FileSystemWatcher>());
            lock (watcherList)
            {
                watcherList.Add(watcher);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[IndexManager] Failed to start watcher for '{fullPath}': {ex.Message}", LogLevel.Warn);
            _ = Task.Run(() => TryRecreateWatcherAsync(pluginId, fullPath, recursive, filterPattern));
        }
    }

    private void RemoveWatcher(string pluginId, FileSystemWatcher watcher)
    {
        try { watcher.Dispose(); } catch { }
        if (_watchers.TryGetValue(pluginId, out var watcherList))
        {
            lock (watcherList)
            {
                watcherList.Remove(watcher);
            }
        }
    }

    private async Task TryRecreateWatcherAsync(string pluginId, string fullPath, bool recursive, string filterPattern)
    {
        // Periodic check to self-heal when U-drives or network NAS comes back online
        while (true)
        {
            await Task.Delay(15000).ConfigureAwait(false); // Check every 15 seconds

            // Check if the plugin registration still exists (do not reconnect if unregistered)
            if (!_registrations.TryGetValue(pluginId, out var list))
                return;

            lock (list)
            {
                if (!list.Any(d => string.Equals(d.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
                    return;
            }

            if (Directory.Exists(fullPath))
            {
                Logger.Log($"[IndexManager] Directory '{fullPath}' resolved back online. Re-creating FileSystemWatcher.");
                CreateWatcher(pluginId, fullPath, recursive, filterPattern);
                PluginSdk.Services.DirectoryIndexerService.NotifyDirectoryChanged(pluginId); // Force load newly connected drive contents
                return;
            }
        }
    }

    public void UnregisterDirectories(string pluginId)
    {
        if (_registrations.TryRemove(pluginId, out _))
        {
            if (_watchers.TryRemove(pluginId, out var watcherList))
            {
                lock (watcherList)
                {
                    foreach (var w in watcherList)
                    {
                        try { w.Dispose(); } catch { }
                    }
                }
            }
            Logger.Log($"[IndexManager] Unregistered all directories for plugin '{pluginId}'.");
        }
    }

    /// <summary>
    /// Searches files within all directories registered by the given plugin.
    /// Uses USN Service for local directories and live directory scans (exempt from exclusion rules if search query matches)
    /// for network drives/unc folders.
    /// </summary>
    public async Task<List<SearchResult>> SearchPluginDirectoriesAsync(string pluginId, string query, CancellationToken token)
    {
        var results = new List<SearchResult>();
        if (!_registrations.TryGetValue(pluginId, out var dirs))
            return results;

        List<MonitoredDir> dirsCopy;
        lock (dirs)
        {
            dirsCopy = new List<MonitoredDir>(dirs);
        }

        var tasks = dirsCopy.Select(dir => SearchDirectoryAsync(dir, query, token));
        var taskResults = await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var subList in taskResults)
        {
            if (subList != null)
                results.AddRange(subList);
        }

        return results;
    }

    private async Task<List<SearchResult>> SearchDirectoryAsync(MonitoredDir dir, string query, CancellationToken token)
    {
        var list = new List<SearchResult>();
        if (!Directory.Exists(dir.Path)) return list;

        var isNetwork = IsNetworkOrSharedPath(dir.Path);
        if (!isNetwork)
        {
            // Local physical drive: Route directly to service USN using SearchDir command (already fully index-watched)
            try
            {
                await _searchService.SearchStreamingAsync(query, 200, 0, dir.Path, result =>
                {
                    if (result.Path.StartsWith(dir.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(result);
                    }
                }, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Log($"[IndexManager] Local directory index query failed for '{dir.Path}': {ex.Message}", LogLevel.Error);
            }
        }
        else
        {
            // Network drive or UNC share: Scan in App memory, matching wildcard
            try
            {
                var files = await Task.Run(() =>
                {
                    var opt = dir.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    return Directory.EnumerateFiles(dir.Path, dir.FilterPattern, opt);
                }, token).ConfigureAwait(false);

                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(file);
                    if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(new SearchResult
                        {
                            Path = file,
                            Name = name,
                            IsDir = false
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[IndexManager] Network directory live scan failed for '{dir.Path}': {ex.Message}", LogLevel.Warn);
            }
        }

        return list;
    }

    private static bool IsNetworkOrSharedPath(string path)
    {
        if (path.StartsWith(@"\\")) return true;
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return false;
            var driveInfo = new DriveInfo(root);
            return driveInfo.DriveType == DriveType.Network;
        }
        catch
        {
            return false;
        }
    }
}
