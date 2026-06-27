using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

internal static class NetworkDriveApplyHelper
{
    public static async Task ApplyChangesAsync(
        SearchService searchService,
        IReadOnlyList<NetworkDriveSetting> previousSettings,
        IReadOnlyList<NetworkDriveSetting> newSettings)
    {
        searchService.ConfigureNetworkIndexes();

        var previousByDrive = previousSettings
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var drive in newSettings
                     .Where(d => !string.IsNullOrWhiteSpace(d.Id))
                     .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.First())
                     .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
        {
            previousByDrive.TryGetValue(drive.Id, out var previous);
            var resolvedDrive = NetworkDriveResolver.GetNetworkDrives()
                .FirstOrDefault(d => string.Equals(NetworkDriveResolver.GetNetworkId(d.Letter), drive.Id, StringComparison.OrdinalIgnoreCase))
                ?.Letter;
            if (previous != null || string.IsNullOrWhiteSpace(resolvedDrive) || searchService.HasNetworkDriveCache(resolvedDrive))
                continue;

            if (await WaitForNetworkIdleAsync(searchService) && searchService.RefreshNetworkDriveIndex(resolvedDrive))
                await WaitForNetworkDriveRefreshAsync(searchService, resolvedDrive);
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
