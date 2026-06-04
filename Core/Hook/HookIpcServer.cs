using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftList.Core.Hook
{
    /// <summary>
    /// Runs inside the hook process.
    /// Connects back to the App's pipe server and sends notifications.
    /// Uses full binary stream protocol (PipeRequestBinarySerializer).
    /// </summary>
    public sealed class HookIpcServer : IDisposable
    {
        private readonly string _pipeName;
        private NamedPipeServerStream? _pipe;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;

        // Fired when the App sends us a "STOP" command
        public event Action? OnStopRequested;

        // Fired when the App sends us a custom command
        public event Action<IpcMessage>? OnCommandReceived;

        public HookIpcServer()
        {
            _pipeName = HookIpcNames.NotifyPipeName;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _listenTask = Task.Run(() => ServerLoop(_cts.Token));
        }

        /// <summary>
        /// Sends a binary message to the connected App.
        /// Thread-safe: uses a write lock.
        /// </summary>
        public void SendMessage(IpcMessage msg)
        {
            try
            {
                lock (_writeLock)
                {
                    if (_pipe != null && _pipe.IsConnected)
                    {
                        PipeRequestBinarySerializer.WriteMessage(_pipe, msg);
                        _pipe.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[HookIpcServer] Failed to send IPC message {msg.Id}: {ex.Message}", LogLevel.Warn);
            }
        }

        public void SendActivate()
        {
            SendMessage(new IpcMessage { Id = IpcMessageId.Activate });
        }

        private readonly object _writeLock = new object();

        private async Task ServerLoop(CancellationToken token)
        {
            Logger.Log($"[HookIpcServer] Starting server on pipe '{_pipeName}'.", LogLevel.Debug);

            System.IO.Pipes.PipeSecurity? pipeSecurity = null;
            try
            {
                pipeSecurity = new System.IO.Pipes.PipeSecurity();
                var everyoneSid = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
                pipeSecurity.AddAccessRule(new System.IO.Pipes.PipeAccessRule(
                    everyoneSid,
                    System.IO.Pipes.PipeAccessRights.ReadWrite | System.IO.Pipes.PipeAccessRights.CreateNewInstance,
                    System.Security.AccessControl.AccessControlType.Allow
                ));

                var authenticatedUsersSid = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.AuthenticatedUserSid, null);
                pipeSecurity.AddAccessRule(new System.IO.Pipes.PipeAccessRule(
                    authenticatedUsersSid,
                    System.IO.Pipes.PipeAccessRights.ReadWrite | System.IO.Pipes.PipeAccessRights.CreateNewInstance,
                    System.Security.AccessControl.AccessControlType.Allow
                ));
                Logger.Log("[HookIpcServer] PipeSecurity successfully configured.", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                Logger.Log($"[HookIpcServer] Failed to create PipeSecurity: {ex.Message}", LogLevel.Warn);
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    NamedPipeServerStream pipe;
                    if (pipeSecurity != null)
                    {
                        pipe = NamedPipeServerStreamAcl.Create(
                            _pipeName,
                            PipeDirection.InOut,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            4096, 4096,
                            pipeSecurity
                        );
                    }
                    else
                    {
                        pipe = new NamedPipeServerStream(
                            _pipeName,
                            PipeDirection.InOut,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            4096, 4096);
                    }

                    lock (_writeLock)
                    {
                        _pipe = pipe;
                    }

                    Logger.Log("[HookIpcServer] Waiting for App to connect...", LogLevel.Debug);
                    await pipe.WaitForConnectionAsync(token);
                    Logger.Log("[HookIpcServer] App connected.", LogLevel.Debug);

                    // Listen for commands from App
                    await ListenForCommands(pipe, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[HookIpcServer] Server loop error: {ex.Message}", LogLevel.Warn);
                    await Task.Delay(2000, token).ConfigureAwait(false);
                }
                finally
                {
                    lock (_writeLock)
                    {
                        _pipe?.Dispose();
                        _pipe = null;
                    }
                }
            }
            Logger.Log("[HookIpcServer] Server loop stopped.", LogLevel.Debug);
        }

        private async Task ListenForCommands(NamedPipeServerStream pipe, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && pipe.IsConnected)
                {
                    IpcMessage msg = await Task.Run(() => PipeRequestBinarySerializer.ReadMessage(pipe), token);
                    Logger.Log($"[HookIpcServer] Received IPC command: {msg.Id}", LogLevel.Debug);
                    if (msg.Id == IpcMessageId.Stop)
                    {
                        OnStopRequested?.Invoke();
                        return;
                    }
                    else
                    {
                        OnCommandReceived?.Invoke(msg);
                    }
                }
            }
            catch (EndOfStreamException) { /* App disconnected */ }
            catch (IOException) { /* App disconnected */ }
            catch (OperationCanceledException) { /* shutting down */ }
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        public void Dispose()
        {
            Stop();
            lock (_writeLock)
            {
                _pipe?.Dispose();
                _pipe = null;
            }
            _cts?.Dispose();
        }
    }
}
