using SwiftList.Core;

namespace SwiftList.App;

// Gives back RuntimeIndex.PathMemo's memory across every live index (local drives, which run in the
// elevated Service process and are reached via pipe; network/WSL/folder indexes, which run in-process).
// PathMemo already self-caps at a high backstop threshold (see Core's PathQueryExtensions), but a search
// window closing/hiding is also a natural point to proactively give the memory back -- called from the
// same spots that already call ShellIconHelper.ClearCache()/Win32Api.TrimWorkingSet() on close/hide.
public static class PathCacheMaintenance
{
    public static void ClearAllPathCaches()
    {
        UserNetworkDriveSearch.ClearAllPathCaches();
        _ = ClearLocalPathCachesAsync();
    }

    private static async Task ClearLocalPathCachesAsync()
    {
        try
        {
            using var searchService = new SearchService();
            await searchService.ClearPathCachesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Log($"[PathCacheMaintenance] Failed to clear local drive path caches: {ex.Message}", LogLevel.Error);
        }
    }
}
