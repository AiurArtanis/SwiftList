using System;
using System.Diagnostics;
using System.IO;
using SwiftList.Core;

namespace SwiftList.App.Services
{
    public static class ServiceInstallManager
    {
        public static string GetServiceExePath()
        {
            string serviceExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SwiftList.Service.exe");
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
                string serviceExePath = GetServiceExePath();
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
                Logger.Log($"[ServiceInstallManager] Service installation failed: {ex}", SwiftList.Core.LogLevel.Error);
                onError?.Invoke(ex);
            }
        }

        public static void SilentInstall(Action onCompleted)
        {
            try
            {
                string serviceExePath = GetServiceExePath();
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
                Logger.Log($"[ServiceInstallManager] Silent service installation failed: {ex.Message}", SwiftList.Core.LogLevel.Error);
            }
            finally
            {
                onCompleted?.Invoke();
            }
        }
    }
}
