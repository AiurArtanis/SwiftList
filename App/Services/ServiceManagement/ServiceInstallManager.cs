using System.Diagnostics;
using System.IO;
using SwiftList.Core;

namespace SwiftList.App.Services;

public static class ServiceInstallManager
{
    private static int _silentInstallInFlight;

    public static string GetServiceExePath()
    {
        var serviceExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SwiftList.Service.exe");
        if (!File.Exists(serviceExePath))
        {
            serviceExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\Service\bin\Debug\net10.0-windows\SwiftList.Service.exe");
        }
        return Path.GetFullPath(serviceExePath);
    }

    public static void InstallService(Action onCompleted, Action<Exception> onError)
    {
        try
        {
            var serviceExePath = GetServiceExePath();
            Logger.Log($"[ServiceInstallManager] Requesting service installation: {serviceExePath} --install");
            var psi = new ProcessStartInfo
            {
                FileName = serviceExePath,
                Arguments = "--install",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var proc = Process.Start(psi);
            proc?.WaitForExit();
            onCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Log($"[ServiceInstallManager] Service installation failed: {ex}", LogLevel.Error);
            onError?.Invoke(ex);
        }
    }

    public static bool SilentInstall(Action onCompleted)
    {
        if (Interlocked.CompareExchange(ref _silentInstallInFlight, 1, 0) != 0)
        {
            Logger.Log("[ServiceInstallManager] Silent service installation already in progress.", LogLevel.Debug);
            return false;
        }

        try
        {
            var serviceExePath = GetServiceExePath();
            Logger.Log($"[ServiceInstallManager] Attempting silent service installation: {serviceExePath}");
            var psi = new ProcessStartInfo
            {
                FileName = serviceExePath,
                Arguments = "--install",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var proc = Process.Start(psi);
            proc?.WaitForExit();
        }
        catch (Exception ex)
        {
            Logger.Log($"[ServiceInstallManager] Silent service installation failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _silentInstallInFlight, 0);
            onCompleted?.Invoke();
        }

        return true;
    }

    /// <summary>
    /// True when SwiftListService is registered and its binary is exactly the exe this build would
    /// install (same full path). A stale path (old version / moved folder) returns false so it gets
    /// reinstalled rather than started against the wrong binary.
    /// </summary>
    public static bool IsInstalledAtCurrentPath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\SwiftListService");
            if (key?.GetValue("ImagePath") is not string imagePath || string.IsNullOrWhiteSpace(imagePath))
                return false;

            var installedExe = ExtractExePath(imagePath);
            if (string.IsNullOrEmpty(installedExe))
                return false;

            return string.Equals(
                Path.GetFullPath(installedExe),
                Path.GetFullPath(GetServiceExePath()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.Log($"[ServiceInstallManager] Failed to read service ImagePath: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    private static string ExtractExePath(string imagePath)
    {
        imagePath = imagePath.Trim();
        if (imagePath.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = imagePath.IndexOf('"', 1);
            return end > 0 ? imagePath.Substring(1, end - 1) : imagePath.Trim('"');
        }
        // Unquoted ImagePath: take up to and including ".exe".
        var exeIdx = imagePath.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIdx >= 0 ? imagePath.Substring(0, exeIdx + 4) : imagePath;
    }

    /// <summary>
    /// Starts the service without elevation, relying on the START permission granted to authenticated
    /// users at install time. Returns true if the service is running afterwards.
    /// </summary>
    public static bool TryStartWithoutElevation()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "start SwiftListService",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
                return false;
            proc.WaitForExit(10000);
            // 0 = started; 1056 = ERROR_SERVICE_ALREADY_RUNNING.
            return proc.ExitCode == 0 || proc.ExitCode == 1056;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ServiceInstallManager] Non-elevated start failed: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    /// <summary>
    /// Fast path before falling back to an elevated (re)install: if the service is already installed
    /// pointing at this build's exe, just start it without a UAC prompt.
    /// </summary>
    public static bool TryStartExistingService()
        => IsInstalledAtCurrentPath() && TryStartWithoutElevation();
}
