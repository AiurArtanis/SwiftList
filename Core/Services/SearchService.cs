using System.IO.Pipes;
using SwiftList.Core.Indexer.Usn;
using SwiftList.Core.Indexer.NetworkDrive;
namespace SwiftList.Core;

public class SearchService : IDisposable
{
    private readonly Dictionary<string, List<SearchResult>> _sessionDirectoryCache = new(StringComparer.OrdinalIgnoreCase);

    private static async Task<NamedPipeClientStream> GetPipeAsync(CancellationToken token)
    {
        var pipe = new NamedPipeClientStream(".", "SwiftListPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1000, token).ConfigureAwait(false);
        return pipe;
    }

    public async Task<UsnIndexer.IndexerStatus> GetStatusAsync(CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.Status }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.Status && resp.Status != null) return resp.Status;
        if (resp.Kind == PipeResponseKind.Error) Logger.Log($"[SearchService] STATUS failed: {resp.Message}", LogLevel.Error);
        return new UsnIndexer.IndexerStatus { State = "error" };
    }

    public async Task<bool> SearchStreamingAsync(string query, int maxResults, int maxAppResults, string? directoryFilter, Action<SearchResult, bool> onResult, CancellationToken token = default)
    {
        var exclusionRules = ExclusionRuleSet.From(UserSettings.Load());
        var fileCandidateLimit = Math.Clamp(maxResults * 4, maxResults, 2000);

        var msg = new SearchRequestMessage
        {
            Id = !string.IsNullOrEmpty(directoryFilter) ? SearchRequestId.SearchDir : SearchRequestId.Search,
            Limit = fileCandidateLimit,
            AppLimit = maxAppResults,
            DirectoryFilter = directoryFilter,
            Query = query
        };

        var parsed = SearchQueryParser.Parse(query);
        string? queryExemptRoot = null;
        if (parsed.IsPathMode && !string.IsNullOrEmpty(parsed.ExactPathLower))
        {
            var resolved = LiveDirectorySearcher.ResolvePathModeSearch(parsed.ExactPathLower);
            queryExemptRoot = !string.IsNullOrEmpty(resolved.DirectoryToScan) ? resolved.DirectoryToScan : parsed.ExactPathLower;
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueOnResult = new Action<SearchResult, bool>((result, isApp) =>
        {
            if (!isApp && FileSystemItemFilter.IsHiddenOrSystem(result.Path))
                return;

            lock (seenPaths)
            {
                if (!seenPaths.Add(result.Path))
                    return;
            }
            onResult(result, isApp);
        });

        var localTask = Task.Run(async () =>
        {
            try
            {
                await SendSearchPipeCommandAsync(msg, (result, isApp) =>
                {
                    if (isApp || !exclusionRules.IsExcluded(result, directoryFilter) || !exclusionRules.IsExcluded(result, queryExemptRoot))
                        uniqueOnResult(result, isApp);
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
                return false;
            }
        }, token);

        var networkTask = Task.Run(() =>
        {
            try
            {
                return SearchNetworkDrives(query, fileCandidateLimit, directoryFilter, exclusionRules, uniqueOnResult, token);
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
            if (!string.IsNullOrEmpty(resolved.DirectoryToScan) && CheckNeedsLiveSearch(resolved.DirectoryToScan, exclusionRules))
            {
                needsLiveSearch = true;
                liveScanDir = resolved.DirectoryToScan;
                liveScanFilter = resolved.FilterQuery;
            }
        }
        else if (!string.IsNullOrEmpty(directoryFilter) && Directory.Exists(directoryFilter) && CheckNeedsLiveSearch(directoryFilter, exclusionRules))
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

    public void RefreshNetworkIndexes() => UserNetworkDriveSearch.Refresh();
    public bool RefreshNetworkDriveIndex(string drive) => UserNetworkDriveSearch.RefreshDrive(drive);
    public IReadOnlyList<NetworkIndexStatus> GetNetworkIndexStatuses() => UserNetworkDriveSearch.GetStatuses();
    public bool HasNetworkDriveCache(string drive) => UserNetworkDriveSearch.HasCache(drive);
    public IReadOnlyList<string> GetCachedNetworkDrives() => UserNetworkDriveSearch.GetCachedDrives();
    public void DeleteNetworkDriveCache(string drive) => UserNetworkDriveSearch.DeleteCache(drive);

    public async Task InitializeOrLoadIndexAsync(bool forceRebuild = false, CancellationToken token = default)
    {
        if (forceRebuild) await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.Rebuild }, token).ConfigureAwait(false);
    }

    public async Task<bool> RebuildDriveIndexAsync(string drive, CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.RebuildDrive, Drive = drive }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.Ok;
    }

    public async Task<bool> DeleteDriveIndexAsync(string drive, CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.DeleteDriveIndex, Drive = drive }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.Ok;
    }

    public async Task<MachineSettings> GetMachineSettingsAsync(CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.GetMachineSettings }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.MachineSettings && resp.MachineSettings != null) return resp.MachineSettings;
        if (resp.Kind == PipeResponseKind.Error) Logger.Log($"[SearchService] GetMachineSettings failed: {resp.Message}", LogLevel.Error);
        return new MachineSettings();
    }

    public async Task<bool> SaveMachineSettingsAsync(MachineSettings settings, CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.SetMachineSettings, MachineSettings = settings }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.Ok;
    }

    private async Task<PipeResponse> SendPipeCommandAsync(SearchRequestMessage msg, CancellationToken token)
    {
        try
        {
            var verboseLog = msg.Id != SearchRequestId.Search && msg.Id != SearchRequestId.SearchDir;
            if (verboseLog)
                Logger.Log($"[PipeClient] Connecting to pipe for command: {msg.Id}...", LogLevel.Debug);
            using var pipe = new NamedPipeClientStream(".", "SwiftListPipe", PipeDirection.InOut, PipeOptions.Asynchronous);

            await pipe.ConnectAsync(1000, token).ConfigureAwait(false);
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

    private static async Task SendSearchPipeCommandAsync(SearchRequestMessage msg, Action<SearchResult, bool> onResult, CancellationToken token)
    {
        using var pipe = await GetPipeAsync(token).ConfigureAwait(false);
        await SearchRequestBinarySerializer.WriteSearchRequestAsync(pipe, msg, token).ConfigureAwait(false);

        await SearchResponseBinarySerializer.ReadAsync(pipe, (result, isApp) =>
        {
            token.ThrowIfCancellationRequested();
            onResult(result, isApp);
        }, token).ConfigureAwait(false);
    }

    private static bool SearchNetworkDrives(string query, int maxResults, string? directoryFilter, ExclusionRuleSet exclusionRules, Action<SearchResult, bool> onResult, CancellationToken token)
    {
        try
        {
            var parsed = SearchQueryParser.Parse(query);
            var queryExemptRoot = parsed.IsPathMode ? parsed.ExactPathLower : null;
            var found = 0;
            UserNetworkDriveSearch.SearchStreaming(query, maxResults, result =>
            {
                token.ThrowIfCancellationRequested();
                if (!exclusionRules.IsExcluded(result, directoryFilter) || !exclusionRules.IsExcluded(result, queryExemptRoot))
                {
                    Interlocked.Increment(ref found);
                    onResult(result, false);
                }
            }, token, directoryFilter);

            return found > 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.Log($"[SearchService] Network drive search failed: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    private static bool CheckNeedsLiveSearch(string dir, ExclusionRuleSet exclusionRules)
    {
        try
        {
            var driveInfo = new DriveInfo(dir);
            if (driveInfo.DriveType == DriveType.Network)
            {
                var letter = dir.Substring(0, 1);
                return !UserSettings.Load().NetworkDrives.Any(d => d.Enabled && string.Equals(d.Drive, letter, StringComparison.OrdinalIgnoreCase));
            }
            // Both NTFS and ReFS are indexed by the USN journal indexer.
            var fs = driveInfo.DriveFormat;
            var isIndexed = string.Equals(fs, "NTFS", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(fs, "ReFS", StringComparison.OrdinalIgnoreCase);
            return !isIndexed
                || exclusionRules.IsExcludedPath(dir, true)
                || exclusionRules.IsExcludedPath(Path.Combine(dir, "_live_search_dummy.txt"), false);
        }
        catch { return true; }
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
