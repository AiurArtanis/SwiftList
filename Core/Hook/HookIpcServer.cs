using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
namespace SwiftList.Core.Hook
{
    /// <summary>
    /// Runs inside the hook process.
    /// Connects back to the App's pipe server and sends notifications.
    /// Uses two isolated named pipes for physically decoupled Event (Out) and Command (In) streams.
    /// </summary>
    public sealed class HookIpcServer : IDisposable
    {
        private NamedPipeServerStream? _eventPipe;
        private NamedPipeServerStream? _cmdPipe;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private readonly Channel<IpcMessage> _sendChannel;

        // Fired when the App sends us a "STOP" command

        public event Action? OnStopRequested;

        // Fired when the App sends us a custom command

        public event Action<IpcMessage>? OnCommandReceived;

        public HookIpcServer()
        {
            _sendChannel = Channel.CreateUnbounded<IpcMessage>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true

            });
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _listenTask = Task.Run(() => ServerLoop(_cts.Token));
        }

        /// <summary>
        /// Sends a binary message to the connected App.
        /// Thread-safe and completely non-blocking: writes to a high-performance Channel.
        /// </summary>
        public void SendMessage(IpcMessage msg)
        {
            _sendChannel.Writer.TryWrite(msg);
        }

        public void SendActivate()
        {
            SendMessage(new IpcMessage { Id = IpcMessageId.Activate });
        }

        private async Task ProcessWriteQueueAsync(NamedPipeServerStream pipe, CancellationToken token)
        {
            try
            {
                var reader = _sendChannel.Reader;
                while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var msg))
                    {
                        if (pipe.IsConnected)
                            await PipeRequestBinarySerializer.WriteMessageAsync(pipe, msg, token).ConfigureAwait(false);
                    }
                }
            }

            catch (OperationCanceledException) { }

            catch (Exception ex)
            {
                Logger.Log($"[HookIpcServer] Write queue error: {ex.Message}", LogLevel.Warn);
            }
        }

        private async Task ServerLoop(CancellationToken token)
        {
            Logger.Log("[HookIpcServer] Starting dual-pipe server loops.", LogLevel.Debug);
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
            }

            catch (Exception ex)
            {
                Logger.Log($"[HookIpcServer] Failed to create PipeSecurity: {ex.Message}", LogLevel.Warn);
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    NamedPipeServerStream eventPipe;
                    NamedPipeServerStream cmdPipe;
                    if (pipeSecurity != null)
                    {
                        eventPipe = NamedPipeServerStreamAcl.Create(

                            HookIpcNames.EventPipeName,
                            PipeDirection.Out,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            4096, 4096,
                            pipeSecurity

                        );

                        cmdPipe = NamedPipeServerStreamAcl.Create(

                            HookIpcNames.CmdPipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            4096, 4096,
                            pipeSecurity

                        );
                    }

                    else
                    {
                        eventPipe = new NamedPipeServerStream(

                            HookIpcNames.EventPipeName,
                            PipeDirection.Out,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            4096, 4096);

                        cmdPipe = new NamedPipeServerStream(

                            HookIpcNames.CmdPipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            4096, 4096);
                    }

                    Logger.Log("[HookIpcServer] Waiting for App to connect on both pipes...", LogLevel.Debug);

                    await Task.WhenAll(

                        eventPipe.WaitForConnectionAsync(token),
                        cmdPipe.WaitForConnectionAsync(token)

                    ).ConfigureAwait(false);
                    Logger.Log("[HookIpcServer] App connected on both pipes.", LogLevel.Debug);
                    _eventPipe = eventPipe;
                    _cmdPipe = cmdPipe;
                    while (_sendChannel.Reader.TryRead(out _)) { }
                    using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(token);

                    var writeTask = ProcessWriteQueueAsync(eventPipe, writeCts.Token);

                    try
                    {
                        await ListenForCommands(cmdPipe, token).ConfigureAwait(false);
                    }

                    finally
                    {
                        writeCts.Cancel();

                        try { await writeTask.ConfigureAwait(false); } catch { }
                    }
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
                    try { _eventPipe?.Dispose(); } catch { }

                    _eventPipe = null;

                    try { _cmdPipe?.Dispose(); } catch { }

                    _cmdPipe = null;
                }
            }

            Logger.Log("[HookIpcServer] Server loops stopped.", LogLevel.Debug);
        }

        private async Task ListenForCommands(NamedPipeServerStream pipe, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && pipe.IsConnected)
                {
                    IpcMessage msg = await PipeRequestBinarySerializer.ReadMessageAsync(pipe, token).ConfigureAwait(false);
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

            try { _eventPipe?.Dispose(); } catch { }

            _eventPipe = null;

            try { _cmdPipe?.Dispose(); } catch { }

            _cmdPipe = null;
            _cts?.Dispose();
        }
    }
}
