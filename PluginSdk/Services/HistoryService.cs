namespace SwiftList.PluginSdk.Services;

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

    private const string AppPrefix = "app:";

    /// <summary>
    /// Whether a history entry refers to a Start-Menu application rather than a file/folder path.
    /// The host marks these with a leading "app:" so a plain <c>File.Exists</c>/<c>Directory.Exists</c>
    /// check never has to run against them -- a packaged app's target can be a virtual
    /// <c>shell:AppsFolder\{AUMID}</c> path, not a real filesystem path.
    /// </summary>
    public static bool IsAppEntry(string entry) => !string.IsNullOrEmpty(entry) && entry.StartsWith(AppPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The entry's underlying path/id with the "app:" marker (if any) stripped -- what to actually
    /// open, display, or (for a non-app entry) check for existence.
    /// </summary>
    public static string GetRawPath(string entry) => IsAppEntry(entry) ? entry.Substring(AppPrefix.Length) : entry;
}
