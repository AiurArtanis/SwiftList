using SwiftList.Core.Hook.InlineSearch;

namespace SwiftList.Core.Hook;

// Split out of HookCommandHandler to keep that file under the line-count limit. Handles
// GetListItems/SelectItem/ClearSelection/GetSelectedIndices, dispatching to ElevatedListControlHelper
// (generic ListBox/ListView interop, used by e.g. the ListSearch plugin) in the Hook process.
internal static class ListControlCommandHandler
{
    public static void HandleGetListItems(HookProcess process, IpcMessage msg)
    {
        var hwnd = (IntPtr)msg.Hwnd;
        var requestId = msg.IntVal;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var items = ElevatedListControlHelper.GetListItems(hwnd);
                const int chunkSize = 500;
                if (items == null || items.Count == 0)
                {
                    process.IpcServer.SendMessage(new IpcMessage
                    {
                        Id = IpcMessageId.GetListItemsResponse,
                        IntVal = requestId,
                        StringArray = Array.Empty<string>(),
                        BoolVal = true
                    });
                    return;
                }

                for (var i = 0; i < items.Count; i += chunkSize)
                {
                    var count = Math.Min(chunkSize, items.Count - i);
                    var chunk = new string[count];
                    items.CopyTo(i, chunk, 0, count);
                    var isFinal = i + count >= items.Count;

                    process.IpcServer.SendMessage(new IpcMessage
                    {
                        Id = IpcMessageId.GetListItemsResponse,
                        IntVal = requestId,
                        StringArray = chunk,
                        BoolVal = isFinal
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[ListControlCommandHandler] GetListItems threw: {ex.Message}", LogLevel.Error);
                // Still terminate the App's stream so it fails fast instead of waiting out the per-chunk timeout.
                process.IpcServer.SendMessage(new IpcMessage
                {
                    Id = IpcMessageId.GetListItemsResponse,
                    IntVal = requestId,
                    StringArray = Array.Empty<string>(),
                    BoolVal = true
                });
            }
        });
    }

    public static void HandleSelectItem(IpcMessage msg)
    {
        var hwnd = (IntPtr)msg.Hwnd;
        var className = msg.StringVal1 ?? string.Empty;
        var index = msg.IntVal;
        var clearOthers = msg.BoolVal;
        var selectState = msg.IsDesktop;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { ElevatedListControlHelper.SelectItem(hwnd, className, index, clearOthers, selectState); }
            catch (Exception ex) { Logger.Log($"[ListControlCommandHandler] SelectItem threw: {ex.Message}", LogLevel.Error); }
        });
    }

    public static void HandleClearSelection(IpcMessage msg)
    {
        var hwnd = (IntPtr)msg.Hwnd;
        var className = msg.StringVal1 ?? string.Empty;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { ElevatedListControlHelper.ClearSelection(hwnd, className); }
            catch (Exception ex) { Logger.Log($"[ListControlCommandHandler] ClearSelection threw: {ex.Message}", LogLevel.Error); }
        });
    }

    public static void HandleGetSelectedIndices(HookProcess process, IpcMessage msg)
    {
        var hwnd = (IntPtr)msg.Hwnd;
        var className = msg.StringVal1 ?? string.Empty;
        var requestId = msg.IntVal;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var indices = Array.Empty<int>();
            try { indices = ElevatedListControlHelper.GetSelectedIndices(hwnd, className).ToArray(); }
            catch (Exception ex) { Logger.Log($"[ListControlCommandHandler] GetSelectedIndices threw: {ex.Message}", LogLevel.Error); }

            process.IpcServer.SendMessage(new IpcMessage
            {
                Id = IpcMessageId.GetSelectedIndicesResponse,
                IntVal = requestId,
                IntArray = indices
            });
        });
    }
}
