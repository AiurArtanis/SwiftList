using System.IO;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Search;

public static class ExplorerSearchHelper
{
    public static Task SearchLocalMatchesAsync(
        SearchService searchService,
        string query,
        int fileLimit,
        int appLimit,
        string contextDirectory,
        List<AppSearchResult> localMatches,
        CancellationToken token) => Task.Run(async () =>
    {
        Logger.Log($"[ExplorerSearchHelper] Starting local search for query: '{query}' in scope: '{contextDirectory}'", LogLevel.Debug);
        var matchCount = 0;
        try
        {
            await searchService.SearchStreamingAsync(query, fileLimit, appLimit, contextDirectory, (result, isApp) =>
            {
                if (!isApp)
                {
                    lock (localMatches)
                    {
                        localMatches.Add(SearchResultMapper.CreateUiResult(result, query, localMatches.Count, isApplication: false, contextDirectory));
                        matchCount++;
                    }
                }
            }, token);
            Logger.Log($"[ExplorerSearchHelper] Local search completed. Matches count: {matchCount}", LogLevel.Debug);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Log($"[ExplorerSearchHelper] Local search failed: {ex.Message}", LogLevel.Error);
        }

        lock (localMatches)
        {
            var normalizedDir = contextDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Filter out the scope directory itself — the backend's StartsWith filter matches it,
            // but it should never appear inside the "Current Folder" results group.
            localMatches.RemoveAll(x =>
                string.Equals(
                    x.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    normalizedDir,
                    StringComparison.OrdinalIgnoreCase));

            var sorted = localMatches
                .OrderBy(x =>
                {
                    var parent = Path.GetDirectoryName(x.FullPath);
                    var normalizedParent = parent?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return string.Equals(normalizedParent, normalizedDir, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                })
                .ThenBy(x => x.FullPath.Length)
                .ToList();

            localMatches.Clear();
            localMatches.AddRange(sorted.Take(50));
            for (var idx = 0; idx < localMatches.Count; idx++)
            {
                localMatches[idx].Index = idx;
            }
        }
    }, token);
}
