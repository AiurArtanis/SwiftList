using System.Diagnostics;

namespace SwiftList.Core.Hook;

public static class HookProcessLauncher
{
    public static Process? Launch(string serviceExePath, bool autoElevate)
    {
        try
        {
            if (!File.Exists(serviceExePath))
            {
                Logger.Log($"[HookIpcClient] Service executable not found: {serviceExePath}", LogLevel.Error);
                return null;
            }

            var psi = new ProcessStartInfo(serviceExePath, "--hook")
            {
                UseShellExecute = autoElevate,
                CreateNoWindow = !autoElevate,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = autoElevate ? "runas" : string.Empty
            };
            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            Logger.Log($"[HookIpcClient] Exception launching hook process: {ex.Message}", LogLevel.Error);
            return null;
        }
    }
}
