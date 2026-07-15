using System.Collections.Concurrent;

namespace SwiftList.Core.Hook;

public static class ListIpcCoordinator
{
    private static readonly object _listQueryLock = new object();
    private static BlockingCollection<string>? _listItemsQueue;
    private static AutoResetEvent? _selectedIndicesEvent;
    private static int[]? _selectedIndicesResult;
    // Echoed back on every response and checked before accepting it, so a reply that arrives after its own
    // call already gave up (timed out) can't be misattributed to a later, unrelated call -- GetListItems
    // and GetSelectedIndices share these fields since only one call is ever in flight at a time (the lock
    // below), but "later call" can still mean the *other* of the two methods.
    private static int _pendingRequestId;
    private static int _nextRequestId;

    public static IEnumerable<string> GetListItems(IntPtr hwnd, Action<IpcMessage> sendMsg)
    {
        lock (_listQueryLock)
        {
            // _pendingRequestId must be updated before _listItemsQueue: AddListItemsChunk (called from a
            // different thread) checks the ID first, so setting the ID last would leave a window where a
            // stale chunk for the previous request could still see the new queue and get pushed into it.
            var requestId = ++_nextRequestId;
            _pendingRequestId = requestId;
            var queue = new BlockingCollection<string>();
            _listItemsQueue = queue;

            sendMsg(new IpcMessage { Id = IpcMessageId.GetListItems, Hwnd = hwnd.ToInt64(), IntVal = requestId });

            while (true)
            {
                if (queue.TryTake(out var item, 2000))
                {
                    if (item == null)
                    {
                        break;
                    }
                    yield return item;
                }
                else
                {
                    break;
                }
            }
        }
    }

    public static IEnumerable<int> GetSelectedIndices(IntPtr hwnd, string className, Action<IpcMessage> sendMsg)
    {
        lock (_listQueryLock)
        {
            // Same ordering requirement as GetListItems above: set the ID before the event/result fields.
            using var evt = new AutoResetEvent(false);
            var requestId = ++_nextRequestId;
            _pendingRequestId = requestId;
            _selectedIndicesEvent = evt;
            _selectedIndicesResult = null;

            sendMsg(new IpcMessage
            {
                Id = IpcMessageId.GetSelectedIndices,
                Hwnd = hwnd.ToInt64(),
                StringVal1 = className,
                IntVal = requestId
            });

            if (evt.WaitOne(1000))
            {
                return _selectedIndicesResult ?? Array.Empty<int>();
            }
            return Array.Empty<int>();
        }
    }

    public static void AddListItemsChunk(int requestId, string[]? chunk, bool isFinal)
    {
        if (requestId != _pendingRequestId) return; // stale reply for a call we already gave up on
        var queue = _listItemsQueue;
        if (queue == null) return;

        if (chunk != null)
        {
            foreach (var item in chunk)
            {
                queue.Add(item);
            }
        }

        if (isFinal)
        {
            queue.Add(null!);
            queue.CompleteAdding();
        }
    }

    public static void SetSelectedIndicesResult(int requestId, int[]? result)
    {
        if (requestId != _pendingRequestId) return; // stale reply for a call we already gave up on
        _selectedIndicesResult = result;
        try
        {
            _selectedIndicesEvent?.Set();
        }
        catch { }
    }
}

