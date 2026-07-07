using SwiftList.Core;
using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Settings;

internal static class NetworkDriveSettingsHelper
{
    public static string GetStateText(ResolvedNetworkDrive? drive, NetworkIndexStatus? indexStatus)
    {
        if (drive != null && !drive.IsReady)
            return TranslationManager.Instance["Network_StatusUnavailable"];

        return indexStatus?.State switch
        {
            "indexing" => TranslationManager.Instance["Network_StatusIndexing"],
            "ready" => TranslationManager.Instance["Network_StatusReady"],
            "cached" => TranslationManager.Instance["Network_StatusCached"],
            "error" => TranslationManager.Instance["Network_StatusError"],
            "pending" => TranslationManager.Instance["Network_StatusPending"],
            _ => TranslationManager.Instance["Network_StatusConnected"]
        };
    }


    public static List<string> GetWslDistros()
    {
        var distros = new List<string>();
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
            if (key != null)
            {
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    var distroName = subKey?.GetValue("DistributionName") as string;
                    if (!string.IsNullOrEmpty(distroName))
                    {
                        // Verify that the network path for the distro is actually accessible via \\wsl$
                        var targetPath = $@"\\wsl$\{distroName}";
                        if (System.IO.Directory.Exists(targetPath))
                        {
                            distros.Add(distroName);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkDriveSettings] Failed to scan WSL distributions via registry: {ex.Message}", LogLevel.Warn);
        }
        return distros;
    }

    public static string NormalizeRefreshMode(string? refreshMode) => refreshMode switch
    {
        "15Minutes" => "15Minutes",
        "Hourly" => "Hourly",
        "Daily" => "Daily",
        _ => "Manual"
    };
}
