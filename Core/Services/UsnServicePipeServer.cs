using System.IO.Pipes;

using SwiftList.Core.Services.HookLaunch;

using SwiftList.Core.Services.Pipe;

using SwiftList.Core.Services.Search;

using SwiftList.Core.Wire;
namespace SwiftList.Core.Services;

using SwiftList.Core;

public sealed class UsnServicePipeServer : IDisposable
{
    private SearchEngine? _engine;
    private CancellationTokenSource? _pipeCts;

    public void Start(SearchEngine engine)
    {
        _engine = engine;
        _pipeCts = new CancellationTokenSource();
        Task.Run(() => PipeServerLoop(_pipeCts.Token));
    }

    public void Stop()
    {
        _pipeCts?.Cancel();
        _pipeCts?.Dispose();
        _pipeCts = null;
        _engine = null;
    }

    private async Task PipeServerLoop(CancellationToken token)
    {
        Logger.Log("[PipeServer] Pipe server loop started.", LogLevel.Debug);
        var pipeSecurity = PipeSecurityFactory.Create();

        // Pre-create 2 parallel listener loops to serve as a connection pool
        var listeners = Enumerable.Range(0, 2)
            .Select(_ => Task.Run(() => ListenLoopAsync(pipeSecurity, token), token))
            .ToArray();

        await Task.WhenAll(listeners).ConfigureAwait(false);
        Logger.Log("[PipeServer] Pipe server loop stopped.");
    }

    private async Task ListenLoopAsync(PipeSecurity? pipeSecurity, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                if (pipeSecurity != null)
                {
                    pipeServer = NamedPipeServerStreamAcl.Create(
                        "SwiftListPipe",
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        65536, 65536,
                        pipeSecurity
                    );
                }
                else
                {
                    pipeServer = new NamedPipeServerStream(
                        "SwiftListPipe",
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        65536, 65536
                    );
                }

                await pipeServer.WaitForConnectionAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(pipeServer, token), token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                pipeServer?.Dispose();
                Logger.Log($"[PipeServer] Server connection failed: {ex.Message}", LogLevel.Error);
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        using (pipe)
        {
            Logger.Log("[PipeServer] Client connected to pipe.", LogLevel.Debug);

            try
            {
                while (!token.IsCancellationRequested && pipe.IsConnected)
                {
                    var request = await SearchRequestBinarySerializer.ReadSearchRequestAsync(pipe, token);
                    var verboseLog = request.Id != SearchRequestId.Search && request.Id != SearchRequestId.SearchDir;
                    if (verboseLog)
                        Logger.Log($"[PipeServer] Request received: {request.Id}", LogLevel.Debug);

                    if (request.Id == SearchRequestId.Search || request.Id == SearchRequestId.SearchDir)
                    {
                        await SearchStreamPump.RunAsync(_engine, request, pipe, token);
                        if (verboseLog)
                            Logger.Log("[PipeServer] Response sent.", LogLevel.Debug);
                        continue;
                    }

                    if (request.Id == SearchRequestId.SubscribeStatus)
                    {
                        await StreamStatusUpdatesAsync(pipe, token).ConfigureAwait(false);
                        continue;
                    }

                    if (request.Id == SearchRequestId.LaunchHook)
                    {
                        var hookResponse = HookLaunchRequestHandler.Handle(pipe, request.RequestElevation);
                        await WriteControlResponseAsync(pipe, hookResponse, token);
                        if (verboseLog)
                            Logger.Log("[PipeServer] Response sent.", LogLevel.Debug);
                        continue;
                    }

                    if (!pipe.IsConnected)
                    {
                        break;
                    }

                    var response = UsnServicePipeRequestProcessor.Process(_engine, request, token);

                    if (verboseLog)
                        Logger.Log($"[PipeServer] Sending response: {response.Kind}...", LogLevel.Debug);
                    await WriteControlResponseAsync(pipe, response, token);
                    if (verboseLog)
                        Logger.Log("[PipeServer] Response sent.", LogLevel.Debug);
                }
            }
            catch (Exception ex) when (IsClientDisconnect(ex))
            {
            }
            catch (Exception ex)
            {
                Logger.Log($"[PipeServer] Client connection handler error: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                try
                {
                    GC.Collect(1, GCCollectionMode.Optimized, blocking: false, compacting: false);
                }
                catch { }
            }
        }

        Logger.Log("[PipeServer] Client disconnected from pipe.", LogLevel.Debug);
    }

    private async Task StreamStatusUpdatesAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        if (_engine == null)
            return;

        var signal = new SemaphoreSlim(0);
        void Handler(Indexer.Usn.UsnIndexer.IndexerStatus _) => signal.Release();

        try
        {
            _engine.StatusChanged += Handler;
            await PipeResponseBinarySerializer.WriteStatusAsync(pipe, _engine.GetStatus(), token).ConfigureAwait(false);

            while (!token.IsCancellationRequested && pipe.IsConnected)
            {
                await signal.WaitAsync(token).ConfigureAwait(false);
                if (!pipe.IsConnected)
                    break;

                await PipeResponseBinarySerializer.WriteStatusAsync(pipe, _engine.GetStatus(), token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (IsClientDisconnect(ex) || ex is OperationCanceledException)
        {
        }
        finally
        {
            _engine.StatusChanged -= Handler;
            signal.Dispose();
        }
    }

    private static Task WriteControlResponseAsync(Stream stream, PipeResponse response, CancellationToken token) => response.Kind switch
    {
        PipeResponseKind.Ok => PipeResponseBinarySerializer.WriteOkAsync(stream, token),
        PipeResponseKind.Error => PipeResponseBinarySerializer.WriteErrorAsync(stream, response.Message, token),
        PipeResponseKind.Status => PipeResponseBinarySerializer.WriteStatusAsync(stream, response.Status ?? new Indexer.Usn.UsnIndexer.IndexerStatus { State = "error" }, token),
        PipeResponseKind.MachineSettings => PipeResponseBinarySerializer.WriteMachineSettingsAsync(stream, response.MachineSettings ?? new MachineSettings(), token),
        PipeResponseKind.FileMetadata => PipeResponseBinarySerializer.WriteFileMetadataAsync(stream, response.FileMetadata ?? new Dictionary<string, FileMetadataEntry>(), token),
        PipeResponseKind.RecentFiles => RecentFilesResponseCodec.WriteRecentFilesAsync(stream, response.RecentFiles ?? new List<SearchResult>(), token),
        PipeResponseKind.HookLaunched => PipeResponseBinarySerializer.WriteHookLaunchAsync(stream, response.Pid, token),
        _ => PipeResponseBinarySerializer.WriteErrorAsync(stream, "Unknown response kind", token)
    };

    private static bool IsClientDisconnect(Exception ex) => ex is EndOfStreamException ||
               ex is IOException ||
               ex.InnerException != null && IsClientDisconnect(ex.InnerException);

    public void Dispose() => Stop();
}
