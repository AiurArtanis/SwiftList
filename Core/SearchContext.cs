namespace SwiftList.Core;

public static class SearchContext
{
    private static readonly AsyncLocal<HashSet<byte>?> _disabledAliasIds = new();

    public static HashSet<byte>? DisabledAliasIds
    {
        get => _disabledAliasIds.Value;
        set => _disabledAliasIds.Value = value;
    }

    private static readonly AsyncLocal<bool?> _fuzzyMatchEnabled = new();

    // Never-set reads as enabled: FzfPattern is also parsed outside a search request (display
    // highlighting, the plugin-facing FuzzyMatchService, tests), and those must keep the historical
    // fuzzy behavior rather than silently inherit whatever the last search on this thread wanted.
    public static bool FuzzyMatchEnabled
    {
        get => _fuzzyMatchEnabled.Value ?? true;
        set => _fuzzyMatchEnabled.Value = value;
    }
}
