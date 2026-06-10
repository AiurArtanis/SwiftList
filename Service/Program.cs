using System.Diagnostics;
using System.ServiceProcess;

using SwiftList.Core;
using SwiftList.Core.Hook;

namespace SwiftList.Service;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Set up global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Logger.Log($"CRITICAL SERVICE UNHANDLED EXCEPTION:\n{e.ExceptionObject}", LogLevel.Error);

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
            var psiStop = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "stop SwiftListService",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psiStop)?.WaitForExit();

            var psiDelete = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "delete SwiftListService",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psiDelete)?.WaitForExit();

            Logger.Log($"Installing service: sc.exe create SwiftListService binPath=\"{serviceExePath} --service\"");

            var psiCreate = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"create SwiftListService binPath= \"\\\"{serviceExePath}\\\" --service\" start= auto DisplayName= \"SwiftList Background Service\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var proc = Process.Start(psiCreate);
            proc?.WaitForExit();

            Logger.Log("Starting service: sc.exe start SwiftListService");
            var psiStart = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "start SwiftListService",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psiStart)?.WaitForExit();

            Console.WriteLine("Service installed and started successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to install service: {ex.Message}");
            Logger.Log($"Failed to install service: {ex}", LogLevel.Error);
        }
    }

    static void UninstallService()
    {
        try
        {
            Logger.Log("Stopping service: sc.exe stop SwiftListService");
            var psiStop = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "stop SwiftListService",
                UseShellExecute = true
            };
            Process.Start(psiStop)?.WaitForExit();

            Logger.Log("Deleting service: sc.exe delete SwiftListService");
            var psiDelete = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "delete SwiftListService",
                UseShellExecute = true
            };
            Process.Start(psiDelete)?.WaitForExit();

            Console.WriteLine("Service uninstalled successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to uninstall service: {ex.Message}");
            Logger.Log($"Failed to uninstall service: {ex}", LogLevel.Error);
        }
    }

    static void RunHookMode()
    {
        var settings = UserSettings.Load();

        // Apply log level from settings
        if (Enum.TryParse<LogLevel>(settings.LogLevel, ignoreCase: true, out var logLevel))
            Logger.MinimumLevel = logLevel;



        // Load plugins to register path collectors in the hook process
        ServicePluginLoader.LoadPlugins();

        Logger.Log($"[HookMode] Starting hook process (elevated={Core.Services.ElevationManager.IsRunningAsAdmin()}).");

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
