using SwiftList.Core.Indexer.NetworkDrive;

namespace SwiftList.Core;

public static class UserNetworkDriveSearch
{
    private static readonly NetworkIndexer NetworkIndexer = new();

    public static void Refresh() => NetworkIndexer.Configure(UserSettings.Load().NetworkDrives, forceRefresh: true);
    public static bool RefreshDrive(string drive) => NetworkIndexer.RefreshDrive(drive);

    public static IReadOnlyList<NetworkIndexStatus> GetStatuses() => NetworkIndexer.GetStatuses();
    public static bool HasCache(string drive) => IndexerHelper.HasCache(drive);
    public static IReadOnlyList<string> GetCachedDrives() => IndexerHelper.GetCachedDrives();
    public static void DeleteCache(string drive) => NetworkIndexer.DeleteCache(drive);


    public static void SearchStreaming(string query, int limit, Action<SearchResult> onResult, CancellationToken token = default, string? directoryFilter = null) => NetworkIndexer.SearchStreaming(query, limit, onResult, token, directoryFilter);
}
