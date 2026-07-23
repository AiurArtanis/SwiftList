namespace SwiftList.Core.Services.Plugin;

/// <summary>
/// Managed core indexer coordinator. Decides if a path should be query-routed
/// to the USN Service via NamedPipe or scanned locally (for network/removable drives).
/// </summary>
public sealed class CoreDirectoryIndexManager
{
    private static readonly Lazy<CoreDirectoryIndexManager> _instance = new(() => new CoreDirectoryIndexManager());
    public static CoreDirectoryIndexManager Instance => _instance.Value;

    private readonly PluginDirectoryWatchRegistry _registry = new();
    private readonly PluginDirectorySearcher _searcher = new();

    private CoreDirectoryIndexManager()
    {
        // Bind the SDK delegates to this manager
        PluginSdk.Services.DirectoryIndexerService.RegisterDirectoryAction = RegisterDirectory;
        PluginSdk.Services.DirectoryIndexerService.UnregisterDirectoriesAction = UnregisterDirectories;
    }

    public void RegisterDirectory(string pluginId, string directoryPath, bool recursive, string filterPattern)
        => _registry.RegisterDirectory(pluginId, directoryPath, recursive, filterPattern);

    public void UnregisterDirectories(string pluginId) => _registry.UnregisterDirectories(pluginId);

    /// <summary>
    /// Searches files within all directories registered by the given plugin.
    /// Uses USN Service for local directories and live directory scans (exempt from exclusion rules if search query matches)
    /// for network drives/unc folders.
    /// </summary>
    public async Task<List<SearchResult>> SearchPluginDirectoriesAsync(string pluginId, string query, CancellationToken token)
    {
        var dirs = _registry.GetDirectories(pluginId);
        if (dirs == null)
            return new List<SearchResult>();

        return await _searcher.SearchAsync(dirs, query, token).ConfigureAwait(false);
    }
}
