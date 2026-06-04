using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
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
                Logger.Log($"[PipeServer] Failed to create PipeSecurity: {ex.Message}");
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
                    Logger.Log($"[PipeServer] Server connection failed: {ex.Message}");
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
                        var request = await Task.Run(() => PipeRequestBinarySerializer.ReadSearchRequest(pipe), token);

                        bool verboseLog = request.Id != SearchRequestId.Search && request.Id != SearchRequestId.SearchDir;
                        if (verboseLog)
                            Logger.Log($"[PipeServer] Request received: {request.Id}", LogLevel.Debug);

                        if (request.Id == SearchRequestId.Search || request.Id == SearchRequestId.SearchDir)
                        {
                            ProcessSearchRequestStreaming(request, pipe, token);
                            if (verboseLog)
                                Logger.Log("[PipeServer] Response sent.", LogLevel.Debug);
                            continue;
                        }

                        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        var responseTask = Task.Run<object>(() => ProcessClientRequest(request, requestCts.Token), requestCts.Token);

                        while (!responseTask.IsCompleted)
                        {
                            if (!pipe.IsConnected)
                            {
                                requestCts.Cancel();
                                break;
                            }

                            await Task.WhenAny(responseTask, Task.Delay(25, token));
                        }

                        object response = await responseTask;
                        if (requestCts.IsCancellationRequested || !pipe.IsConnected)
                        {
                            break;
                        }

                        string textResponse = (string)response;
                        if (verboseLog)
                            Logger.Log($"[PipeServer] Sending response (length: {textResponse.Length})...", LogLevel.Debug);

                        PipeResponseBinarySerializer.WriteText(pipe, textResponse);

                        if (verboseLog)
                            Logger.Log("[PipeServer] Response sent.", LogLevel.Debug);
                    }
                }
                catch (Exception ex) when (IsClientDisconnect(ex))
                {
                }
                catch (Exception ex)
                {
                    Logger.Log($"[PipeServer] Client connection handler error: {ex.Message}");
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

        private string ProcessClientRequest(SearchRequestMessage msg, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                switch (msg.Id)
                {
                    case SearchRequestId.Status:
                        var status = _engine?.GetStatus();
                        return JsonSerializer.Serialize(status);

                    case SearchRequestId.Rebuild:
                        Logger.Log("[UsnService] Received REBUILD request from client.");
                        _engine?.InitializeOrLoadIndex(true);
                        return "OK";

                    case SearchRequestId.GetMachineSettings:
                        return JsonSerializer.Serialize(_engine?.GetMachineSettings() ?? new MachineSettings());

                    case SearchRequestId.SetMachineSettings:
                        var settings = JsonSerializer.Deserialize<MachineSettings>(msg.JsonSettings ?? string.Empty);
                        if (settings == null)
                            return "ERROR: Invalid settings";

                        Logger.Log("[UsnService] Received SET_MACHINE_SETTINGS request.");
                        _engine?.UpdateMachineSettings(settings);
                        return "OK";
                }
                return "ERROR: Unknown command";
            }
            catch (OperationCanceledException)
            {
                return "CANCELLED";
            }
            catch (Exception ex)
            {
                Logger.Log($"[UsnService] Error processing request {msg.Id}: {ex.Message}");
                return $"ERROR: {ex.Message}";
            }
        }

        private void ProcessSearchRequestStreaming(SearchRequestMessage msg, Stream stream, CancellationToken token)
        {
            using var writer = SearchResponseBinarySerializer.CreateWriter(stream);
            try
            {
                token.ThrowIfCancellationRequested();

                void WriteResult(SearchResult result, bool isApp)
                {
                    token.ThrowIfCancellationRequested();
                    if (isApp)
                        SearchResponseBinarySerializer.WriteAppResult(writer, result);
                    else
                        SearchResponseBinarySerializer.WriteFileResult(writer, result);
                    writer.Flush();
                }

                if (msg.Id == SearchRequestId.SearchDir)
                {
                    _engine?.SearchStreaming(msg.Query ?? string.Empty, msg.Limit, msg.AppLimit, msg.DirectoryFilter, WriteResult, token);
                }
                else
                {
                    _engine?.SearchStreaming(msg.Query ?? string.Empty, msg.Limit, msg.AppLimit, null, WriteResult, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (IsClientDisconnect(ex))
            {
            }
            catch (Exception ex)
            {
                Logger.Log($"[UsnService] Error processing streaming search request {msg.Id}: {ex.Message}");
            }
            finally
            {
                try
                {
                    SearchResponseBinarySerializer.WriteEnd(writer);
                }
                catch (Exception ex) when (IsClientDisconnect(ex))
                {
                }
            }
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
