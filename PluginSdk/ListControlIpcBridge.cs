namespace SwiftList.PluginSdk;

public static class ListControlIpcBridge
{
    public static Func<IntPtr, IEnumerable<string>>? GetListItemsFunc { get; set; }
    public static Func<IntPtr, string, IEnumerable<int>>? GetSelectedIndicesFunc { get; set; }
    public static Action<IntPtr, string, int, bool, bool>? SelectItemAction { get; set; }
    public static Action<IntPtr, string>? ClearSelectionAction { get; set; }
}
