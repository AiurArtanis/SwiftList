using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftList.Core.Hook
{
    /// <summary>
    /// Runs inside the App process.
    /// Launches SwiftList.Service.exe --hook, then connects to the hook process's
    /// pipe server and listens for events and commands.
    /// </summary>
    public sealed class HookIpcClient : IDisposable
    {
        private readonly string _serviceExePath;
        private readonly bool _autoElevate;
        private Process? _hookProcess;
        private NamedPipeClientStream? _eventPipe;
        private NamedPipeClientStream? _cmdPipe;
        private BinaryWriter? _writer;
        private BinaryReader? _reader;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private readonly object _writeLock = new object();

        /// <summary>
        /// The process ID of the launched hook service. Zero if not yet started.
        /// </summary>
        public int ServiceProcessId { get; private set; }

        private bool _isHotkeysDisabled;
        public bool IsHotkeysDisabled
        {
            get => _isHotkeysDisabled;
            set
            {
                if (_isHotkeysDisabled != value)
                {
                    _isHotkeysDisabled = value;
                    SendMessage(new IpcMessage { Id = IpcMessageId.SetHotkeysDisabled, BoolVal = value });
                }
            }
        }

        // Hook and Tracker Events fired to App
        public event Action? OnActivated;
        public event Action<char>? OnCharacterTyped;
        public event Action? OnBackspacePressed;
        public event Action? OnEscapePressed;
        public event Action? OnEnterPressed;
        public event Action? OnUpPressed;
        public event Action? OnDownPressed;
        public event Action? OnLeftPressed;
        public event Action? OnRightPressed;
        public event Action<int>? OnCtrlNumberPressed;
        public event Action<int, int>? OnMouseClick;
        public event Action<IntPtr, string, string, bool>? OnExplorerActivated;
        public event Action? OnExplorerDeactivated;
        public event Action<string, bool>? OnPathCaptured;
        public event Action? OnActiveWindowMoved;
        public event Action<string>? OnError;

        public HookIpcClient(string serviceExePath, bool autoElevate)
        {
            _serviceExePath = serviceExePath;
            _autoElevate = autoElevate;
        }

        public void Start()
        {
            if (_cts != null) return; // already started
            _cts = new CancellationTokenSource();
            _listenTask = Task.Run(() => RunLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                SendMessage(new IpcMessage { Id = IpcMessageId.Stop });
            }
            catch { }
            finally
            {
                lock (_writeLock)
                {
                    _eventPipe?.Dispose();
                    _eventPipe = null;
                    _cmdPipe?.Dispose();
                    _cmdPipe = null;
                }
            }
        }

        /// <summary>
        /// Sends a structured binary message to the hook service.
        /// </summary>
        public void SendMessage(IpcMessage msg)
        {
            try
            {
                lock (_writeLock)
                {
                    if (_cmdPipe != null && _cmdPipe.IsConnected && _writer != null)
                    {
                        PipeRequestBinarySerializer.WriteMessage(_writer, msg);
                        _cmdPipe.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[HookIpcClient] Failed to send IPC message {msg.Id}: {ex.Message}", LogLevel.Warn);
            }
        }

        private async Task RunLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _hookProcess = LaunchHookProcess();
                    if (_hookProcess == null)
                    {
                        Logger.Log("[HookIpcClient] Failed to launch hook process.", LogLevel.Error);
                        await Task.Delay(5000, token);
                        continue;
                    }

                    ServiceProcessId = _hookProcess.Id;
                    Logger.Log($"[HookIpcClient] Hook process launched (PID {_hookProcess.Id}), connecting to Event and Cmd pipes...", LogLevel.Debug);

                    await Task.Delay(500, token);

                    using var eventPipe = new NamedPipeClientStream(".", HookIpcNames.EventPipeName, PipeDirection.In, PipeOptions.Asynchronous);
                    using var cmdPipe = new NamedPipeClientStream(".", HookIpcNames.CmdPipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                    lock (_writeLock)
                    {
                        _eventPipe = eventPipe;
                        _cmdPipe = cmdPipe;
                    }

                    await Task.WhenAll(
                        eventPipe.ConnectAsync(5000, token),
                        cmdPipe.ConnectAsync(5000, token)
                    ).ConfigureAwait(false);

                    Logger.Log("[HookIpcClient] Connected to hook pipes.", LogLevel.Debug);

                    lock (_writeLock)
                    {
                        _writer = new BinaryWriter(cmdPipe, System.Text.Encoding.UTF8, leaveOpen: true);
                        _reader = new BinaryReader(eventPipe, System.Text.Encoding.UTF8, leaveOpen: true);
                    }

                    // Send initial process ID of the App so the Service can ignore it
                    SendMessage(new IpcMessage { Id = IpcMessageId.SetAppProcessId, ProcessId = (uint)Environment.ProcessId });
                    SendMessage(new IpcMessage { Id = IpcMessageId.SetHotkeysDisabled, BoolVal = _isHotkeysDisabled });

                    // Listen for events from Hook Service
                    while (!token.IsCancellationRequested && eventPipe.IsConnected && !_hookProcess.HasExited)
                    {
                        IpcMessage msg = await Task.Run(() => PipeRequestBinarySerializer.ReadMessage(_reader), token);
                        DispatchEvent(msg);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (TimeoutException)
                {
                    Logger.Log("[HookIpcClient] Timeout connecting to hook pipe; will retry.", LogLevel.Warn);
                }
                catch (EndOfStreamException)
                {
                    Logger.Log("[HookIpcClient] Hook process disconnected (EOF); will restart.", LogLevel.Warn);
                }
                catch (IOException ex)
                {
                    Logger.Log($"[HookIpcClient] Pipe IO error: {ex.Message}; will restart.", LogLevel.Warn);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[HookIpcClient] Unexpected error: {ex.Message}; will restart.", LogLevel.Warn);
                }
                finally
                {
                    lock (_writeLock)
                    {
                        try { _writer?.Dispose(); } catch { }
                        _writer = null;
                        try { _reader?.Dispose(); } catch { }
                        _reader = null;
                        _eventPipe = null;
                        _cmdPipe = null;
                    }
                    try { _hookProcess?.Kill(); } catch { }
                    _hookProcess = null;
                }

                if (!token.IsCancellationRequested)
                {
                    await Task.Delay(2000, token).ConfigureAwait(false);
                }
            }

            Logger.Log("[HookIpcClient] Loop exited.", LogLevel.Debug);
        }

        private void DispatchEvent(IpcMessage msg)
        {
            try
            {
                switch (msg.Id)
                {
                    case IpcMessageId.Activate: OnActivated?.Invoke(); break;
                    case IpcMessageId.KeyBackspace: OnBackspacePressed?.Invoke(); break;
                    case IpcMessageId.KeyEscape: OnEscapePressed?.Invoke(); break;
                    case IpcMessageId.KeyEnter: OnEnterPressed?.Invoke(); break;
                    case IpcMessageId.KeyUp: OnUpPressed?.Invoke(); break;
                    case IpcMessageId.KeyDown: OnDownPressed?.Invoke(); break;
                    case IpcMessageId.KeyLeft: OnLeftPressed?.Invoke(); break;
                    case IpcMessageId.KeyRight: OnRightPressed?.Invoke(); break;
                    case IpcMessageId.ExplorerDeactivated: OnExplorerDeactivated?.Invoke(); break;
                    case IpcMessageId.ActiveWindowMoved: OnActiveWindowMoved?.Invoke(); break;
                    case IpcMessageId.KeyChar:
                        OnCharacterTyped?.Invoke(msg.CharVal);
                        break;
                    case IpcMessageId.KeyCtrlNumber:
                        OnCtrlNumberPressed?.Invoke(msg.IntVal);
                        break;
                    case IpcMessageId.MouseClick:
                        OnMouseClick?.Invoke(msg.MouseX, msg.MouseY);
                        break;
                    case IpcMessageId.ExplorerActivated:
                        OnExplorerActivated?.Invoke(new IntPtr(msg.Hwnd), msg.StringVal1 ?? string.Empty, msg.StringVal2 ?? string.Empty, msg.IsDesktop);
                        break;
                    case IpcMessageId.PathCaptured:
                        OnPathCaptured?.Invoke(msg.StringVal1 ?? string.Empty, msg.IsDesktop);
                        break;
                    case IpcMessageId.Error:
                        OnError?.Invoke(msg.StringVal1 ?? string.Empty);
                        break;
                    case IpcMessageId.GetListItemsResponse:
                        ListIpcCoordinator.SetListItemsResult(msg.StringArray);
                        break;
                    case IpcMessageId.GetSelectedIndicesResponse:
                        ListIpcCoordinator.SetSelectedIndicesResult(msg.IntArray);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[HookIpcClient] Error dispatching IPC message {msg.Id}: {ex.Message}", LogLevel.Warn);
            }
        }

        private Process? LaunchHookProcess()
        {
            try
            {
                if (!File.Exists(_serviceExePath))
                {
                    Logger.Log($"[HookIpcClient] Service executable not found: {_serviceExePath}", LogLevel.Error);
                    return null;
                }

                var psi = new ProcessStartInfo(_serviceExePath, "--hook")
                {
                    UseShellExecute = _autoElevate,
                    CreateNoWindow = !_autoElevate,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Verb = _autoElevate ? "runas" : string.Empty
                };

                return Process.Start(psi);
            }
            catch (Exception ex)
            {
                Logger.Log($"[HookIpcClient] Exception launching hook process: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
