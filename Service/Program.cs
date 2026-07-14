using System.Diagnostics;
using System.ServiceProcess;

using SwiftList.Core;
using SwiftList.Core.Hook;
using SwiftList.Core.Services;

namespace SwiftList.Service;

static class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    // -4 is DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 (winuser.h); only used by RunHookMode below.
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [STAThread]
    static void Main(string[] args)
    {
        // Set up global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Logger.Log($"CRITICAL SERVICE UNHANDLED EXCEPTION:\n{e.ExceptionObject}", LogLevel.Error);

        // Wire up plugin logger to the core logger
        PluginSdk.Logger.LogAction = (msg, lvl) => Logger.Log(msg, (LogLevel)(int)lvl);

        var isHook = args.Length > 0 && args[0].Equals("--hook", StringComparison.OrdinalIgnoreCase);
        if (isHook)
        {
            Logger.Initialize("hook.log", Logger.UserDataDir, overwrite: true);
            Logger.Log("=========================================");
            Logger.Log($"Hook starting with arguments: {string.Join(" ", args)}");
        }
        else
        {
            Logger.Initialize("service.log", Logger.SharedDataDir, overwrite: true);
            Logger.Log("=========================================");
            Logger.Log($"Service starting with arguments: {string.Join(" ", args)}");
        }

        if (args.Length > 0)
        {
            var cmd = args[0].ToLowerInvariant();
            if (cmd == "--service")
            {
                Logger.Log("Running as Windows Service.");
                ServiceBase.Run(new UsnService());
                return;
            }
            else if (cmd == "--install" || cmd == "-i")
            {
                Logger.Log("Executing service installation.");
                InstallService();
                return;
            }
            else if (cmd == "--uninstall" || cmd == "-u")
            {
                Logger.Log("Executing service uninstallation.");
                UninstallService();
                return;
            }
            else if (cmd == "--hook")
            {
                Logger.Log("Running in hook mode.");
                RunHookMode();
                return;
            }
        }

        // Default fallback: Debug Console Mode
        Logger.Log("Running in debug console mode.");
        Console.WriteLine("SwiftList Background Service is running. Press Ctrl+C to exit.");

        using var service = new UsnServiceDebugWrapper();
        service.Start();

        var quitEvent = new ManualResetEvent(false);
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            eventArgs.Cancel = true;
            quitEvent.Set();
        };
        quitEvent.WaitOne();
        service.Stop();
    }

    static void InstallService()
    {
        try
        {
            var serviceExePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SwiftList.Service.exe");
            serviceExePath = Path.GetFullPath(serviceExePath);

            Console.WriteLine($"Installing service from path: {serviceExePath}");

            // Clean up any existing service instance to prevent "1073: service already exists" errors
            Logger.Log("Cleaning up existing service instance before install.");
            ServiceControlRunner.Run("stop SwiftListService", 0, 1060, 1062);
            ServiceControlRunner.Run("delete SwiftListService", 0, 1060);

            Logger.Log($"Installing service: sc.exe create SwiftListService binPath=\"{serviceExePath} --service\"");

            var create = ServiceControlRunner.Run($"create SwiftListService binPath= \"\\\"{serviceExePath}\\\" --service\" start= auto DisplayName= \"SwiftList Background Service\"");
            if (!create.IsSuccess(0))
                throw new InvalidOperationException("sc create failed. See service.log for details.");

            // Grant all authenticated users START/STOP/QUERY on the service so the non-elevated app can
            // start and stop it without a UAC prompt every time. Install is already elevated here, so this
            // one-time descriptor change is free. SYSTEM and Administrators keep full control.
            // AU ACE = CC LC SW RP WP LO RC = query-config/status, enum-deps, start, stop, interrogate, read.
            Logger.Log("Setting service security descriptor to allow non-admin start/stop.");
            var sdset = ServiceControlRunner.Run("sdset SwiftListService D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)(A;;CCLCSWRPWPLORC;;;AU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)");
            if (!sdset.IsSuccess(0))
                Logger.Log("[ServiceInstaller] Service was created but sdset failed; non-admin start/stop may require elevation.", LogLevel.Warn);

            Logger.Log("Starting service: sc.exe start SwiftListService");
            var start = ServiceControlRunner.Run("start SwiftListService", 0, 1056);
            if (!start.IsSuccess(0, 1056))
                throw new InvalidOperationException("sc start failed. See service.log for details.");

            Console.WriteLine("Service installed and started successfully!");
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            Console.WriteLine($"Failed to install service: {ex.Message}");
            Logger.Log($"Failed to install service: {ex}", LogLevel.Error);
        }
    }

    static void UninstallService()
    {
        try
        {
            Logger.Log("Stopping service: sc.exe stop SwiftListService");
            ServiceControlRunner.Run("stop SwiftListService", 0, 1060, 1062);

            Logger.Log("Deleting service: sc.exe delete SwiftListService");
            var delete = ServiceControlRunner.Run("delete SwiftListService", 0, 1060);
            if (!delete.IsSuccess(0, 1060))
                throw new InvalidOperationException("sc delete failed. See service.log for details.");

            Console.WriteLine("Service uninstalled successfully!");
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            Console.WriteLine($"Failed to uninstall service: {ex.Message}");
            Logger.Log($"Failed to uninstall service: {ex}", LogLevel.Error);
        }
    }

    static void RunHookMode()
    {
        // Without this, this process defaults to DPI-unaware, so GetWindowRect/GetMonitorInfo return
        // coordinates virtualized down to 96 DPI while DwmGetWindowAttribute returns true physical
        // pixels -- the two stop matching at any scaling above 100%, which is why FullscreenHelper's
        // "does the foreground window's rect equal the monitor's rect" check silently failed for
        // fullscreen video at 150% scaling (reported bug: quick window still summonable during
        // PotPlayer fullscreen playback). Only this mode calls FullscreenHelper, so this is scoped
        // here rather than in Main. Best-effort since the API is only available on Windows 10 1703+.
        try { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { /* best-effort */ }

        var settings = UserSettings.Load();

        // Apply log level from settings
        if (Enum.TryParse<LogLevel>(settings.LogLevel, ignoreCase: true, out var logLevel))
            Logger.MinimumLevel = logLevel;



        // Load plugins to register path collectors in the hook process
        ServicePluginLoader.LoadForHook();

        Logger.Log($"[HookMode] Starting hook process (elevated={ElevationManager.IsRunningAsAdmin()}).");

        using var ipcServer = new HookIpcServer();
        using var hookProcess = new HookProcess(ipcServer);

        ipcServer.OnStopRequested += () =>
        {
            Logger.Log("[HookMode] Stop requested by App.");
            hookProcess.Stop();
        };

        ipcServer.Start();

        // Block on the Win32 message loop (installs hook inside)
        hookProcess.RunMessageLoop();

        Logger.Log("[HookMode] Message loop exited; shutting down hook mode.");
    }
}

class UsnServiceDebugWrapper : IDisposable
{
    private readonly UsnService _service = new UsnService();
    public void Start() => _service.TestStart();
    public void Stop() => _service.TestStop();
    public void Dispose() => _service.Dispose();
}
