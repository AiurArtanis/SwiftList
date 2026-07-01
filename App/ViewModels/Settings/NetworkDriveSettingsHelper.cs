using SwiftList.Core;
using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Settings;

public sealed record RefreshModeOption(string Value, string Label)
{
    public override string ToString() => Label;
}

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

    public static string GetWslUncPrefix()
    {
        if (System.IO.Directory.Exists(@"\\wsl.localhost"))
            return @"\\wsl.localhost";
        return @"\\wsl$";
    }

    public static List<string> GetWslDistros()
    {
        var distros = new List<string>();
        try
        {
            var searchPath = GetWslUncPrefix();

            if (searchPath != null)
            {
                foreach (var dir in System.IO.Directory.GetDirectories(searchPath))
                {
                    distros.Add(System.IO.Path.GetFileName(dir));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkDriveSettings] Failed to scan WSL distributions: {ex.Message}", LogLevel.Warn);
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
