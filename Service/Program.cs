using System.ServiceProcess;

using SwiftList.Core;

namespace SwiftList.Service;

static class Program
{
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
                ServiceInstaller.Install();
                return;
            }
            else if (cmd == "--uninstall" || cmd == "-u")
            {
                Logger.Log("Executing service uninstallation.");
                ServiceInstaller.Uninstall();
                return;
            }
            else if (cmd == "--hook")
            {
                Logger.Log("Running in hook mode.");
                HookModeLauncher.Run();
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
}

class UsnServiceDebugWrapper : IDisposable
{
    private readonly UsnService _service = new UsnService();
    public void Start() => _service.TestStart();
    public void Stop() => _service.TestStop();
    public void Dispose() => _service.Dispose();
}
