using System.IO.Pipes;
using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core;

// Split across SearchService.cs (status/search/recent-files) and SearchService.Management.cs (drive and
// settings admin pass-throughs) to stay under the repo's per-file line limit -- both halves are one type.
public partial class SearchService : IDisposable
{
    private readonly Dictionary<string, List<SearchResult>> _sessionDirectoryCache = new(StringComparer.OrdinalIgnoreCase);

    private static async Task<NamedPipeClientStream> GetPipeAsync(CancellationToken token)
    {
        var pipe = new NamedPipeClientStream(".", "SwiftListPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2000, token).ConfigureAwait(false);
        return pipe;
    }

    public async Task<UsnIndexer.IndexerStatus> GetStatusAsync(CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.Status }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.Status && resp.Status != null) return resp.Status;
        if (resp.Kind == PipeResponseKind.Error) Logger.Log($"[SearchService] STATUS failed: {resp.Message}", LogLevel.Error);
        return new UsnIndexer.IndexerStatus { State = "error" };
    }

    public async Task<bool> PingAsync(CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.Ping }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.Ok;
    }

    public async Task<bool> SearchStreamingAsync(string query, int maxResults, int maxAppResults, string? directoryFilter, Action<SearchResult> onResult, CancellationToken token = default, Action? onLocalSearchFailed = null)
    {
        var exclusionRules = ExclusionRuleSet.From(UserSettings.Load());
        var fileCandidateLimit = Math.Clamp(maxResults * 4, maxResults, 2000);

        var isSearchDir = !string.IsNullOrEmpty(directoryFilter);
        var msg = new SearchRequestMessage
        {
            Id = isSearchDir ? SearchRequestId.SearchDir : SearchRequestId.Search,
            Limit = fileCandidateLimit,
            AppLimit = maxAppResults,
            DirectoryFilter = isSearchDir ? directoryFilter : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Query = query,
            DisabledAliasComponents = UserSettings.Load().DisabledPluginComponents
                .Where(c => c.Contains("::AliasProvider::", StringComparison.OrdinalIgnoreCase))
                .ToList()
        };

        HashSet<byte>? disabledIds = null;
        if (msg.DisabledAliasComponents != null && msg.DisabledAliasComponents.Count > 0)
        {
            disabledIds = new HashSet<byte>();
            foreach (var comp in msg.DisabledAliasComponents)
            {
                var id = AliasProviderRegistry.GetProviderIdByComponentId(comp);
                if (id != 255)
                    disabledIds.Add(id);
            }
        }
        SearchContext.DisabledAliasIds = disabledIds;

        var parsed = SearchQueryParser.Parse(query);
        string? queryExemptRoot = null;
        if (parsed.IsPathMode && !string.IsNullOrEmpty(parsed.ExactPathLower))
        {
            var resolved = LiveDirectorySearcher.ResolvePathModeSearch(parsed.ExactPathLower);
            queryExemptRoot = !string.IsNullOrEmpty(resolved.DirectoryToScan) ? resolved.DirectoryToScan : parsed.ExactPathLower;
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueOnResult = new Action<SearchResult>(result =>
        {
            if (FileSystemItemFilter.IsHiddenOrSystem(result))
                return;

            lock (seenPaths)
            {
                if (!seenPaths.Add(result.Path))
                    return;
            }
            onResult(result);
        });

        var localTask = Task.Run(async () =>
        {
            try
            {
                await SendSearchPipeCommandAsync(msg, result =>
                {
                    if (!exclusionRules.IsExcluded(result, directoryFilter) || !exclusionRules.IsExcluded(result, queryExemptRoot))
                        uniqueOnResult(result);
                }, token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchService] Streaming local search failed: {ex.Message}", LogLevel.Error);
                onLocalSearchFailed?.Invoke();
                return false;
            }
        }, token);

        var networkTask = Task.Run(() =>
        {
            try
            {
                return SearchServiceHelper.SearchNetworkDrives(query, fileCandidateLimit, directoryFilter, exclusionRules, uniqueOnResult, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchService] Network drive search failed: {ex.Message}", LogLevel.Error);
                return false;
            }
        }, token);

        var needsLiveSearch = false;
        var liveScanDir = string.Empty;
        var liveScanFilter = string.Empty;

        if (parsed.IsPathMode && !string.IsNullOrEmpty(parsed.ExactPathLower))
        {
            var resolved = LiveDirectorySearcher.ResolvePathModeSearch(parsed.ExactPathLower);
            if (!string.IsNullOrEmpty(resolved.DirectoryToScan) && SearchServiceHelper.CheckNeedsLiveSearch(resolved.DirectoryToScan, exclusionRules))
            {
                needsLiveSearch = true;
                liveScanDir = resolved.DirectoryToScan;
                liveScanFilter = resolved.FilterQuery;
            }
        }
        else if (!string.IsNullOrEmpty(directoryFilter) && Directory.Exists(directoryFilter) && SearchServiceHelper.CheckNeedsLiveSearch(directoryFilter, exclusionRules))
        {
            needsLiveSearch = true;
            liveScanDir = directoryFilter;
            liveScanFilter = query;
        }

        Task<bool>? liveTask = null;
        if (needsLiveSearch && !string.IsNullOrEmpty(liveScanDir))
        {
            liveTask = Task.Run(() =>
            {
                try
                {
                    List<SearchResult> entries;
                    lock (this)
                    {
                        if (_sessionDirectoryCache.TryGetValue(liveScanDir, out var cached))
                        {
                            entries = cached;
                        }
                        else
                        {
                            entries = LiveDirectorySearcher.ScanDirectory(liveScanDir, 10000, token);
                            // Bound the per-session directory cache so long-running sessions that scope
                            // searches into many directories don't retain them all indefinitely.
                            if (_sessionDirectoryCache.Count > 32)
                                _sessionDirectoryCache.Clear();
                            _sessionDirectoryCache[liveScanDir] = entries;
                        }
                    }
                    var onlyDirectChildren = parsed.IsPathMode && string.IsNullOrEmpty(liveScanFilter);
                    return LiveDirectorySearcher.MatchAndStream(entries, liveScanFilter, uniqueOnResult, token, onlyDirectChildren, liveScanDir);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Logger.Log($"[SearchService] Live directory search failed: {ex.Message}", LogLevel.Error);
                    return false;
                }
            }, token);
        }

        var tasks = new List<Task<bool>> { localTask, networkTask };
        if (liveTask != null) tasks.Add(liveTask);

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Any(r => r);
    }

    // In-memory index lookup only (no disk I/O) -- the most recently created entries across all of the
    // given directories' subtrees, most recent first. The elevated service only tracks local drive
    // letters, so network/WSL directories are queried in-process here (same split as SearchStreamingAsync's
    // localTask/networkTask) and merged by actual creation time rather than just concatenated.
    public async Task<List<SearchResult>> GetRecentFilesAsync(IReadOnlyList<string> directories, int limit, int maxAgeMinutes, CancellationToken token = default)
    {
        var networkTask = Task.Run(() =>
        {
            try
            {
                return UserNetworkDriveSearch.GetRecentFiles(directories, limit, maxAgeMinutes);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchService] Network drive GetRecentFiles failed: {ex.Message}", LogLevel.Error);
                return new List<SearchResult>();
            }
        }, token);

        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.GetRecentFiles, Directories = directories.ToList(), Limit = limit, MaxAgeMinutes = maxAgeMinutes }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.Error) Logger.Log($"[SearchService] GetRecentFiles failed: {resp.Message}", LogLevel.Error);
        var localResults = resp.Kind == PipeResponseKind.RecentFiles && resp.RecentFiles != null ? resp.RecentFiles : new List<SearchResult>();

        var networkResults = await networkTask.ConfigureAwait(false);
        var merged = localResults.Concat(networkResults).OrderByDescending(r => r.CreatedUtc);
        return (limit > 0 ? merged.Take(limit) : merged).ToList();
    }

    private async Task<PipeResponse> SendPipeCommandAsync(SearchRequestMessage msg, CancellationToken token)
    {
        try
        {
            var verboseLog = msg.Id != SearchRequestId.Search && msg.Id != SearchRequestId.SearchDir;
            if (verboseLog)
                Logger.Log($"[PipeClient] Connecting to pipe for command: {msg.Id}...", LogLevel.Debug);
            using var pipe = new NamedPipeClientStream(".", "SwiftListPipe", PipeDirection.InOut, PipeOptions.Asynchronous);

            await pipe.ConnectAsync(2000, token).ConfigureAwait(false);
            if (verboseLog)
                Logger.Log("[PipeClient] Connected. Writing command...", LogLevel.Debug);
            await SearchRequestBinarySerializer.WriteSearchRequestAsync(pipe, msg, token).ConfigureAwait(false);
            if (verboseLog)
                Logger.Log("[PipeClient] Command written. Reading response...", LogLevel.Debug);
            var resp = await PipeResponseBinarySerializer.ReadAsync(pipe, token).ConfigureAwait(false);
            if (verboseLog)
                Logger.Log($"[PipeClient] Response received: {resp.Kind}.", LogLevel.Debug);
            return resp;
        }
        catch (Exception ex)
        {
            Logger.Log($"[PipeClient] SendPipeCommand failed for {msg.Id}: {ex.Message}", LogLevel.Error);
            return new PipeResponse { Kind = PipeResponseKind.Error, Message = ex.Message };
        }
    }

    private static async Task SendSearchPipeCommandAsync(SearchRequestMessage msg, Action<SearchResult> onResult, CancellationToken token)
    {
        using var pipe = await GetPipeAsync(token).ConfigureAwait(false);
        await SearchRequestBinarySerializer.WriteSearchRequestAsync(pipe, msg, token).ConfigureAwait(false);

        await SearchResponseBinarySerializer.ReadAsync(pipe, result =>
        {
            token.ThrowIfCancellationRequested();
            onResult(result);
        }, token).ConfigureAwait(false);
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
