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

        drive = drive.Trim();
        if (drive.StartsWith(@"\\") || drive.StartsWith(@"//"))
        {
            var normalized = drive.Replace('/', '\\');
            return normalized.TrimEnd('\\');
        }

        var letter = char.ToUpperInvariant(drive[0]);
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

    public static string GetCachePath(string drive) => NetworkDriveCacheLocator.GetCachePath(drive);
    public static bool HasCache(string drive) => NetworkDriveCacheLocator.HasCache(drive);
    public static IReadOnlyList<string> GetCachedDrives() => NetworkDriveCacheLocator.GetCachedDrives();
    public static void DeleteCache(string drive) => NetworkDriveCacheLocator.DeleteCache(drive);

    public static bool TryLoad(string drive, out NetworkIndex index)
        => NetworkDriveCacheLocator.TryLoad(drive, out index);

    public static void Save(NetworkIndex index) => NetworkDriveCacheLocator.Save(index);
}
