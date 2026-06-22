using System.IO.Pipes;
using SwiftList.Core.Indexer.Usn;
using SwiftList.Core.Indexer.NetworkDrive;
namespace SwiftList.Core;

public class SearchService : IDisposable
{
    private static NamedPipeClientStream? _preconnectedPipe;
    private static readonly object _pipeLock = new();
    private static Task? _preconnectTask;

    public SearchService() => StartPreconnect();

    private static void StartPreconnect()
    {
        lock (_pipeLock)
        {
            if (_preconnectedPipe != null && _preconnectedPipe.IsConnected)
                return;
            if (_preconnectTask != null && !_preconnectTask.IsCompleted)
                return;

            _preconnectTask = Task.Run(async () =>
            {
                try
                {
                    var pipe = new NamedPipeClientStream(".", "SwiftListPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
                    await pipe.ConnectAsync(5000).ConfigureAwait(false);
                    lock (_pipeLock)
                    {
                        if (_preconnectedPipe != null)
                        {
                            try { _preconnectedPipe.Dispose(); } catch { }
                        }
                        _preconnectedPipe = pipe;
                    }
                }
                catch
                {
                    // Silent catch for background preconnection failures
                }
            });
        }
    }

    private static async Task<NamedPipeClientStream> GetPipeAsync(CancellationToken token)
    {
        NamedPipeClientStream? pipe = null;
        lock (_pipeLock)
        {
            if (_preconnectedPipe != null && _preconnectedPipe.IsConnected)
            {
                pipe = _preconnectedPipe;
                _preconnectedPipe = null;
            }
        }

        if (pipe != null)
        {
            StartPreconnect();
            return pipe;
        }

        pipe = new NamedPipeClientStream(".", "SwiftListPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1000, token).ConfigureAwait(false);
        StartPreconnect();
        return pipe;
    }

    public async Task<UsnIndexer.IndexerStatus> GetStatusAsync(CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.Status }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.Status && resp.Status != null)
            return resp.Status;
        if (resp.Kind == PipeResponseKind.Error)
            Logger.Log($"[SearchService] STATUS failed: {resp.Message}", LogLevel.Error);
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

        var localTask = Task.Run(async () =>
        {
            try
            {
                await SendSearchPipeCommandAsync(msg, (result, isApp) =>
                {
                    if (isApp || !exclusionRules.IsExcluded(result))
                        onResult(result, isApp);
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
                return SearchNetworkDrives(query, fileCandidateLimit, directoryFilter, exclusionRules, onResult, token);
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

        var results = await Task.WhenAll(localTask, networkTask).ConfigureAwait(false);
        return results[0] || results[1];
    }

    public void RefreshNetworkIndexes() => UserNetworkDriveSearch.Refresh();

    public IReadOnlyList<NetworkIndexStatus> GetNetworkIndexStatuses() => UserNetworkDriveSearch.GetStatuses();

    public async Task InitializeOrLoadIndexAsync(bool forceRebuild = false, CancellationToken token = default)
    {
        if (forceRebuild)
        {
            await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.Rebuild }, token).ConfigureAwait(false);
        }
    }

    public async Task<MachineSettings> GetMachineSettingsAsync(CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.GetMachineSettings }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.MachineSettings && resp.MachineSettings != null)
            return resp.MachineSettings;
        if (resp.Kind == PipeResponseKind.Error)
            Logger.Log($"[SearchService] GetMachineSettings failed: {resp.Message}", LogLevel.Error);
        return new MachineSettings();
    }

    public async Task<bool> SaveMachineSettingsAsync(MachineSettings settings, CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(

            new SearchRequestMessage { Id = SearchRequestId.SetMachineSettings, MachineSettings = settings },

            token).ConfigureAwait(false);
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
        NamedPipeClientStream pipe;
        try
        {
            pipe = await GetPipeAsync(token).ConfigureAwait(false);
        }
        catch
        {
            pipe = new NamedPipeClientStream(".", "SwiftListPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(1000, token).ConfigureAwait(false);
        }

        using (pipe)
        {
            await SearchRequestBinarySerializer.WriteSearchRequestAsync(pipe, msg, token).ConfigureAwait(false);

            await SearchResponseBinarySerializer.ReadAsync(pipe, (result, isApp) =>
            {
                token.ThrowIfCancellationRequested();
                onResult(result, isApp);
            }, token).ConfigureAwait(false);
        }
    }

    private static bool SearchNetworkDrives(string query, int maxResults, string? directoryFilter, ExclusionRuleSet exclusionRules, Action<SearchResult, bool> onResult, CancellationToken token)
    {
        try
        {
            var found = 0;
            UserNetworkDriveSearch.SearchStreaming(query, maxResults, result =>
            {
                token.ThrowIfCancellationRequested();
                if (!exclusionRules.IsExcluded(result))
                {
                    Interlocked.Increment(ref found);
                    onResult(result, false);
                }
            }, token, directoryFilter);

            return found > 0;
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
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
