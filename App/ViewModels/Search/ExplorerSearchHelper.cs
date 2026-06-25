using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwiftList.Core;
using SwiftList.App.Services;

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
        CancellationToken token)
    {
        return Task.Run(async () =>
        {
            await searchService.SearchStreamingAsync(query, fileLimit, appLimit, contextDirectory, (result, isApp) =>
            {
                if (!isApp)
                {
                    lock (localMatches)
                    {
                        localMatches.Add(SearchResultMapper.CreateUiResult(result, query, localMatches.Count, isApplication: false, contextDirectory));
                    }
                }
            }, token);

            lock (localMatches)
            {
                var normalizedDir = contextDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
                localMatches.AddRange(sorted);
                for (var idx = 0; idx < localMatches.Count; idx++)
                {
                    localMatches[idx].Index = idx;
                }
            }
        }, token);
    }
}
