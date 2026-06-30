namespace SwiftList.PluginSdk.Services;

/// <summary>
/// A decoupled service allowing plugins to register custom directories for global indexing and real-time monitoring.
/// </summary>
public static class DirectoryIndexerService
{
    /// <summary>
    /// Delegate set by the host application to handle directory registration.
    /// Parameters: (pluginId, directoryPath, recursive, filterPattern)
    /// </summary>
    public static Action<string, string, bool, string>? RegisterDirectoryAction { get; set; }

    /// <summary>
    /// Delegate set by the host application to clear directory registrations for a plugin.
    /// Parameters: (pluginId)
    /// </summary>
    public static Action<string>? UnregisterDirectoriesAction { get; set; }

    /// <summary>
    /// Event fired when a monitored directory's content changes.
    /// Parameters: (pluginId)
    /// </summary>
    public static event Action<string>? DirectoryChanged;

    /// <summary>
    /// Invokes the DirectoryChanged event. Should only be called by the host application.
    /// </summary>
    public static void NotifyDirectoryChanged(string pluginId) => DirectoryChanged?.Invoke(pluginId);

    /// <summary>
    /// Registers a directory to be indexed and monitored by the host system (service or app manager).
    /// </summary>
    public static void RegisterDirectory(string pluginId, string directoryPath, bool recursive = true, string filterPattern = "*") => RegisterDirectoryAction?.Invoke(pluginId, directoryPath, recursive, filterPattern);

    /// <summary>
    /// Unregisters all directories registered by the specified plugin.
    /// </summary>
    public static void UnregisterDirectories(string pluginId) => UnregisterDirectoriesAction?.Invoke(pluginId);
}
