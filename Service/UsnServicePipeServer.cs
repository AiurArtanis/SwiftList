using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using SwiftList.Core;
namespace SwiftList.Service
{
    internal sealed class UsnServicePipeServer : IDisposable
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
            PipeSecurity? pipeSecurity = null;

            try
            {
                pipeSecurity = new PipeSecurity();
                var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

                pipeSecurity.AddAccessRule(new PipeAccessRule(

                    everyoneSid,
                    PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                    AccessControlType.Allow

                ));
                var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

                pipeSecurity.AddAccessRule(new PipeAccessRule(

                    authenticatedUsersSid,
                    PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                    AccessControlType.Allow

                ));
                Logger.Log("[PipeServer] PipeSecurity successfully configured.", LogLevel.Debug);
            }

            catch (Exception ex)
            {
                Logger.Log($"[PipeServer] Failed to create PipeSecurity: {ex.Message}", SwiftList.Core.LogLevel.Error);
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    NamedPipeServerStream pipeServer;
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

                    await pipeServer.WaitForConnectionAsync(token);
                    _ = Task.Run(() => HandleClientAsync(pipeServer, token), token);
                }

                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.Log($"[PipeServer] Server connection failed: {ex.Message}", SwiftList.Core.LogLevel.Error);
                    await Task.Delay(1000, token);
                }
            }

            Logger.Log("[PipeServer] Pipe server loop stopped.");
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
                        bool verboseLog = request.Id != SearchRequestId.Search && request.Id != SearchRequestId.SearchDir;
                        if (verboseLog)
                            Logger.Log($"[PipeServer] Request received: {request.Id}", LogLevel.Debug);
                        if (request.Id == SearchRequestId.Search || request.Id == SearchRequestId.SearchDir)
                        {
                            await SearchStreamPump.RunAsync(_engine, request, pipe, token);
                            if (verboseLog)
                                Logger.Log("[PipeServer] Response sent.", LogLevel.Debug);
                            continue;
                        }

                        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(token);

                        var responseTask = Task.Run(() => ProcessClientRequest(request, requestCts.Token), requestCts.Token);
                        while (!responseTask.IsCompleted)
                        {
                            if (!pipe.IsConnected)
                            {
                                requestCts.Cancel();
                                break;
                            }

                            await Task.WhenAny(responseTask, Task.Delay(25, token));
                        }

                        var response = await responseTask;
                        if (requestCts.IsCancellationRequested || !pipe.IsConnected)
                        {
                            break;
                        }

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
                    Logger.Log($"[PipeServer] Client connection handler error: {ex.Message}", SwiftList.Core.LogLevel.Error);
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

        private PipeResponse ProcessClientRequest(SearchRequestMessage msg, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                switch (msg.Id)
                {
                    case SearchRequestId.Status:
                        var status = _engine?.GetStatus();
                        return new PipeResponse
                        {
                            Kind = PipeResponseKind.Status,
                            Status = status ?? new SwiftList.Core.Indexer.Usn.UsnIndexer.IndexerStatus { State = "error" }

                        };

                    case SearchRequestId.Rebuild:
                        Logger.Log("[UsnService] Received REBUILD request from client.");
                        _engine?.InitializeOrLoadIndex(true);
                        return new PipeResponse { Kind = PipeResponseKind.Ok };

                    case SearchRequestId.GetMachineSettings:
                        return new PipeResponse
                        {
                            Kind = PipeResponseKind.MachineSettings,
                            MachineSettings = _engine?.GetMachineSettings() ?? new MachineSettings()

                        };

                    case SearchRequestId.SetMachineSettings:
                        var settings = msg.MachineSettings;
                        if (settings == null)
                            return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Invalid settings" };
                        Logger.Log("[UsnService] Received SET_MACHINE_SETTINGS request.");
                        _engine?.UpdateMachineSettings(settings);
                        return new PipeResponse { Kind = PipeResponseKind.Ok };
                }

                return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Unknown command" };
            }

            catch (OperationCanceledException)
            {
                return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Cancelled" };
            }

            catch (Exception ex)
            {
                Logger.Log($"[UsnService] Error processing request {msg.Id}: {ex.Message}", SwiftList.Core.LogLevel.Error);
                return new PipeResponse { Kind = PipeResponseKind.Error, Message = ex.Message };
            }
        }

        private static Task WriteControlResponseAsync(Stream stream, PipeResponse response, CancellationToken token)
        {
            return response.Kind switch
            {
                PipeResponseKind.Ok => PipeResponseBinarySerializer.WriteOkAsync(stream, token),
                PipeResponseKind.Error => PipeResponseBinarySerializer.WriteErrorAsync(stream, response.Message, token),
                PipeResponseKind.Status => PipeResponseBinarySerializer.WriteStatusAsync(stream, response.Status ?? new SwiftList.Core.Indexer.Usn.UsnIndexer.IndexerStatus { State = "error" }, token),

                PipeResponseKind.MachineSettings => PipeResponseBinarySerializer.WriteMachineSettingsAsync(stream, response.MachineSettings ?? new MachineSettings(), token),
                _ => PipeResponseBinarySerializer.WriteErrorAsync(stream, "Unknown response kind", token)

            };
        }

        private static bool IsClientDisconnect(Exception ex)
        {
            return ex is EndOfStreamException ||

                   ex is IOException ||

                   ex.InnerException != null && IsClientDisconnect(ex.InnerException);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
