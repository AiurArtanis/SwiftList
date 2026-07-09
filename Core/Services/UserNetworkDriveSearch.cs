using SwiftList.Core.Indexer.NetworkDrive;

namespace SwiftList.Core;

public static class UserNetworkDriveSearch
{
    private static readonly NetworkIndexer NetworkIndexer = new();
    public static event Action<IReadOnlyList<NetworkIndexStatus>>? StatusesChanged
    {
        add => NetworkIndexer.StatusesChanged += value;
        remove => NetworkIndexer.StatusesChanged -= value;
    }

    public static void Configure()
    {
        var settings = UserSettings.Load();
        NetworkIndexer.Configure(settings.NetworkDrives, settings.WslSettings, settings.FolderIndexes);
    }

    public static void Refresh()
    {
        var settings = UserSettings.Load();
        NetworkIndexer.Configure(settings.NetworkDrives, settings.WslSettings, settings.FolderIndexes, forceRefresh: true);
    }
    public static bool RefreshDrive(string drive) => NetworkIndexer.RefreshDrive(drive);
    public static bool CancelDrive(string drive) => NetworkIndexer.CancelDrive(drive);

    public static IReadOnlyList<NetworkIndexStatus> GetStatuses() => NetworkIndexer.GetStatuses();
    public static bool HasCache(string drive) => IndexerHelper.HasCache(drive);
    public static IReadOnlyList<string> GetCachedDrives() => IndexerHelper.GetCachedDrives();
    public static void DeleteCache(string drive) => NetworkIndexer.DeleteCache(drive);


    public static void SearchStreaming(string query, int limit, Action<SearchResult> onResult, CancellationToken token = default, string? directoryFilter = null) => NetworkIndexer.SearchStreaming(query, limit, onResult, token, directoryFilter);

    public static List<SearchResult> GetRecentFiles(IReadOnlyList<string> directories, int limit, int maxAgeMinutes) => NetworkIndexer.GetRecentFiles(directories, limit, maxAgeMinutes);
}
