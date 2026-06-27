namespace SwiftList.Core;

public static class SearchContext
{
    private static readonly AsyncLocal<HashSet<byte>?> _disabledAliasIds = new();

    public static HashSet<byte>? DisabledAliasIds
    {
        get => _disabledAliasIds.Value;
        set => _disabledAliasIds.Value = value;
    }
}
