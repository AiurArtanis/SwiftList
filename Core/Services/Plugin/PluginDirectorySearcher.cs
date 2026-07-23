using SwiftList.Core.Services.Search;

namespace SwiftList.Core.Services.Plugin;

/// <summary>
/// Answers a plugin's directory search by routing each registered directory to either the USN-service
/// pipe (local drives, already fully index-watched) or an in-memory Directory.EnumerateFiles scan
/// (network/UNC shares). Kept separate from <see cref="PluginDirectoryWatchRegistry"/>, which owns
/// registration and FileSystemWatcher lifecycle -- answering "what matches this query" is a different
/// concern from "watch for changes."
/// </summary>
internal sealed class PluginDirectorySearcher
{
    private readonly SearchService _searchService = new();

    public async Task<List<SearchResult>> SearchAsync(IReadOnlyList<MonitoredDir> dirs, string query, CancellationToken token)
    {
        var results = new List<SearchResult>();
        var tasks = dirs.Select(dir => SearchDirectoryAsync(dir, query, token));
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
