namespace SwiftList.Core.Indexer.NetworkDrive;

internal static class IndexerHelper
{
    public static string? NormalizeFilter(string? directoryFilter)
    {
        if (string.IsNullOrWhiteSpace(directoryFilter))
            return null;

        var value = directoryFilter.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
        return value.EndsWith(Path.DirectorySeparatorChar) ? value : value + Path.DirectorySeparatorChar;
    }

    public static string NormalizeDrive(string drive)
    {
        if (string.IsNullOrWhiteSpace(drive))
            return string.Empty;

        var letter = char.ToUpperInvariant(drive.Trim()[0]);
        return char.IsLetter(letter) ? letter.ToString() : string.Empty;
    }

    public static string NormalizeRefreshMode(string? refreshMode) => refreshMode switch
    {
        "15Minutes" => "15Minutes",
        "Hourly" => "Hourly",
        "Daily" => "Daily",
        _ => "Manual"
    };

    public static TimeSpan? GetRefreshInterval(string refreshMode) => refreshMode switch
    {
        "15Minutes" => TimeSpan.FromMinutes(15),
        "Hourly" => TimeSpan.FromHours(1),
        "Daily" => TimeSpan.FromDays(1),
        _ => null
    };

    public static string GetCachePath(string drive) => FileRecordStoreSerializer.GetBasePath(Path.Combine(Logger.UserDataDir, "indexes"), drive) + ".meta";
    public static bool HasCache(string drive) => FileRecordStoreSerializer.Exists(Path.Combine(Logger.UserDataDir, "indexes"), drive);
    public static IReadOnlyList<string> GetCachedDrives() => FileRecordStoreSerializer.ListSourceKeys(Path.Combine(Logger.UserDataDir, "indexes"));
    public static void DeleteCache(string drive) => FileRecordStoreSerializer.Delete(Path.Combine(Logger.UserDataDir, "indexes"), drive);

    public static bool TryLoad(string drive, out NetworkIndex index)
    {
        index = new NetworkIndex(drive);
        var store = FileRecordStoreSerializer.Load(Path.Combine(Logger.UserDataDir, "indexes"), drive);
        if (store == null)
            return false;

        try
        {
            index = NetworkIndex.FromStore(store);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[IndexerHelper] Failed to load network drive {drive}: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    public static void Save(NetworkIndex index) => FileRecordStoreSerializer.Save(Path.Combine(Logger.UserDataDir, "indexes"), index.ToStore());
}
