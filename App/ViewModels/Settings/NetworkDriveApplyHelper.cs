using SwiftList.App.Services;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

internal static class NetworkDriveApplyHelper
{
    public static async Task ApplyChangesAsync(
        SearchService searchService,
        IReadOnlyList<NetworkDriveSetting> previousSettings,
        IReadOnlyList<NetworkDriveSetting> newSettings)
    {
        var previousByDrive = previousSettings
            .GroupBy(d => d.Drive, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(d => d.Drive, StringComparer.OrdinalIgnoreCase);

        foreach (var drive in newSettings
                     .GroupBy(d => d.Drive, StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.First())
                     .OrderBy(d => d.Drive, StringComparer.OrdinalIgnoreCase))
        {
            if (!drive.Enabled)
                continue;

            previousByDrive.TryGetValue(drive.Drive, out var previous);
            var wasEnabled = previous?.Enabled == true;
            if (wasEnabled || searchService.HasNetworkDriveCache(drive.Drive))
                continue;

            if (await WaitForNetworkIdleAsync(searchService) && searchService.RefreshNetworkDriveIndex(drive.Drive))
                await WaitForNetworkDriveRefreshAsync(searchService, drive.Drive);
        }
    }

    private static async Task<bool> WaitForNetworkIdleAsync(SearchService searchService)
    {
        for (var i = 0; i < 120; i++)
        {
            if (!searchService.GetNetworkIndexStatuses().Any(s => s.State is "pending" or "indexing"))
                return true;

            await Task.Delay(500);
        }

        return false;
    }

    private static async Task WaitForNetworkDriveRefreshAsync(SearchService searchService, string drive)
    {
        for (var i = 0; i < 120; i++)
        {
            var status = searchService.GetNetworkIndexStatuses()
                .FirstOrDefault(s => s.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
            if (status?.State is not ("pending" or "indexing"))
                return;

            await Task.Delay(500);
        }
    }
}
