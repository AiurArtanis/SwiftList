namespace SwiftList.PluginSdk;

/// <summary>
/// A decoupled service to retrieve search and navigation history from the host application.
/// </summary>
public static class HistoryService
{
    /// <summary>
    /// Delegate function set by the host application to retrieve history paths.
    /// </summary>
    public static Func<IEnumerable<string>>? GetHistoryPathsFunc { get; set; }

    /// <summary>
    /// Retrieves the list of recently visited paths.
    /// </summary>
    public static IEnumerable<string> GetHistoryPaths() => GetHistoryPathsFunc?.Invoke() ?? Array.Empty<string>();
}
