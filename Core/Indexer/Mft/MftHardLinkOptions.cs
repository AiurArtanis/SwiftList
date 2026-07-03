namespace SwiftList.Core.Indexer.Mft;

/// <summary>
/// Feature flag for building NTFS indexes by parsing the raw $MFT (full one-to-many hard-link
/// support) instead of FSCTL_ENUM_USN_DATA. Off by default; enable with the environment variable
/// SWIFTLIST_MFT_HARDLINKS=1, or set <see cref="Enabled"/> at runtime. The $MFT path falls back to
/// USN enumeration on any failure, so enabling it is safe.
/// </summary>
public static class MftHardLinkOptions
{
    public static bool Enabled { get; set; } =
        string.Equals(Environment.GetEnvironmentVariable("SWIFTLIST_MFT_HARDLINKS"), "1", StringComparison.Ordinal);
}
