using SwiftList.Core.Indexer.NetworkDrive;

namespace SwiftList.Core;

public static class UserNetworkDriveSearch
{
    private static readonly NetworkIndexer NetworkIndexer = new();

    public static void Refresh() => NetworkIndexer.Configure(UserSettings.Load().NetworkDrives, forceRefresh: true);

    public static IReadOnlyList<NetworkIndexStatus> GetStatuses() => NetworkIndexer.GetStatuses();

    public static List<SearchResult> Search(string query, int limit, CancellationToken token = default, string? directoryFilter = null) => NetworkIndexer.Search(query, limit, token, directoryFilter);

    public static void SearchStreaming(string query, int limit, Action<SearchResult> onResult, CancellationToken token = default, string? directoryFilter = null) => NetworkIndexer.SearchStreaming(query, limit, onResult, token, directoryFilter);
}
