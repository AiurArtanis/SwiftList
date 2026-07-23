using SwiftList.Core;

namespace SwiftList.Service;

// Windows service install/uninstall via sc.exe (through ServiceControlRunner), including the one-time
// security-descriptor change that lets the non-elevated App start/stop the service without a UAC prompt.
// Kept separate from Program's CLI dispatch and hook-mode bootstrap -- service lifecycle administration
// has nothing to do with either of those.
static class ServiceInstaller
{
    public static void Install()
    {
        try
        {
            var serviceExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
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

    public static void Uninstall()
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
}
