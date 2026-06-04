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
        private NamedPipeClientStream? _pipe;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private readonly object _writeLock = new object();

        /// <summary>
        /// The process ID of the launched hook service. Zero if not yet started.
        /// </summary>
        public int ServiceProcessId { get; private set; }

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
                    _pipe?.Dispose();
                    _pipe = null;
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
                    if (_pipe != null && _pipe.IsConnected)
                    {
                        PipeRequestBinarySerializer.WriteMessage(_pipe, msg);
                        _pipe.Flush();
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
            string pipeName = HookIpcNames.NotifyPipeName;

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
                    Logger.Log($"[HookIpcClient] Hook process launched (PID {_hookProcess.Id}), connecting to pipe '{pipeName}'...", LogLevel.Debug);

                    await Task.Delay(500, token);

                    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    lock (_writeLock)
                    {
                        _pipe = pipe;
                    }

                    await pipe.ConnectAsync(5000, token);
                    Logger.Log("[HookIpcClient] Connected to hook pipe.", LogLevel.Debug);

                    // Send initial process ID of the App so the Service can ignore it
                    SendMessage(new IpcMessage { Id = IpcMessageId.SetAppProcessId, ProcessId = (uint)Environment.ProcessId });

                    // Listen for events from Hook Service
                    while (!token.IsCancellationRequested && pipe.IsConnected && !_hookProcess.HasExited)
                    {
                        IpcMessage msg = await Task.Run(() => PipeRequestBinarySerializer.ReadMessage(pipe), token);
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
                        _pipe = null;
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

                var psi = new ProcessStartInfo
                {
                    FileName = _serviceExePath,
                    Arguments = "--hook",
                    UseShellExecute = _autoElevate,
                    CreateNoWindow = !_autoElevate,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                if (_autoElevate)
                {
                    psi.Verb = "runas";
                }

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
