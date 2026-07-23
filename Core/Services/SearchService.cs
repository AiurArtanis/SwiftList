using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core;

public class SearchService : IDisposable
{
    private readonly Dictionary<string, List<SearchResult>> _sessionDirectoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SearchPipeClient _pipeClient = new();

    public Task<UsnIndexer.IndexerStatus> GetStatusAsync(CancellationToken token = default) => _pipeClient.GetStatusAsync(token);

    public Task<bool> PingAsync(CancellationToken token = default) => _pipeClient.PingAsync(token);

    // Asks the already-running --service instance to spawn the hook process directly into this caller's
    // own session (see HookProcessBroker) -- the App itself never launches the hook process anymore, so
    // it never has a "runas" UAC prompt of its own to show. requestElevation is only honored server-side
    // when that session's user is genuinely an administrator; otherwise it just launches non-elevated.
    public Task<(bool Ok, int Pid, string? Error)> RequestHookLaunchAsync(bool requestElevation, CancellationToken token = default)
        => _pipeClient.RequestHookLaunchAsync(requestElevation, token);

    // Fire-and-forget, called whenever a search window closes/hides (mirrors ShellIconHelper.ClearCache()'s
    // existing trigger points) -- gives back the local drives' per-row full-path memo, which otherwise
    // only self-clears once it crosses its own high backstop threshold (see PathQueryExtensions).
    public Task ClearPathCachesAsync(CancellationToken token = default) => _pipeClient.ClearPathCachesAsync(token);

    // bypassExclusions: opts this one search out of ExcludedPaths/IgnoredPathGlobs/IgnoredPathRegexes
    // filtering. The caller is responsible for stripping whatever query-string marker triggers this
    // (see SearchQuerySortParser.StripExclusionBypass) BEFORE calling here -- `query` itself is always
    // matched/highlighted verbatim, so a caller must never pass the marker through as part of it. Only
    // covers results that are already indexed (local NTFS/ReFS drives, plus whatever network/WSL data
    // already made it into the index) -- content under an excluded network/WSL root was never indexed
    // in the first place (WalkFilter skips it at build time), so this can't recover that without a live
    // filesystem walk, which is deliberately out of scope here.
    public async Task<bool> SearchStreamingAsync(string query, int maxResults, int maxAppResults, string? directoryFilter, Action<SearchResult> onResult, CancellationToken token = default, Action? onLocalSearchFailed = null, bool bypassExclusions = false)
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
            // Unconditional, even in bypass mode: "*" only opts out of the user's own
            // ExcludedPaths/Globs/Regexes configuration, not hidden/system attributes -- those are a
            // separate, always-on filter.
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
                await SearchPipeClient.SendSearchPipeCommandAsync(msg, result =>
                {
                    if (bypassExclusions || !exclusionRules.IsExcluded(result, directoryFilter) || !exclusionRules.IsExcluded(result, queryExemptRoot))
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
                return SearchServiceHelper.SearchNetworkDrives(query, fileCandidateLimit, directoryFilter, exclusionRules, bypassExclusions, uniqueOnResult, token);
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

    // In-memory index lookup only (no disk I/O) -- the most recently modified entries across all of the
    // given directories' subtrees, most recent first. The elevated service only tracks local drive
    // letters, so network/WSL directories are queried in-process here (same split as SearchStreamingAsync's
    // localTask/networkTask) and merged by actual modification time rather than just concatenated.
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
        var merged = localResults.Concat(networkResults).OrderByDescending(r => r.Metadata.Modified);
        return (limit > 0 ? merged.Take(limit) : merged).ToList();
    }

    // Forwards to the pipe client -- kept on SearchService itself since SearchServiceManagementExtensions
    // and other callers already reach this as an instance method (`service.SendPipeCommandAsync(...)`).
    internal Task<PipeResponse> SendPipeCommandAsync(SearchRequestMessage msg, CancellationToken token)
        => _pipeClient.SendPipeCommandAsync(msg, token);

    public void Dispose() => GC.SuppressFinalize(this);
}
