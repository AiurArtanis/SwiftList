using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;

namespace SwiftList.Core.Hook
{
    public static class ListIpcCoordinator
    {
        private static readonly object _listQueryLock = new object();
        private static BlockingCollection<string>? _listItemsQueue;
        private static AutoResetEvent? _selectedIndicesEvent;
        private static int[]? _selectedIndicesResult;

        public static IEnumerable<string> GetListItems(IntPtr hwnd, Action<IpcMessage> sendMsg)
        {
            lock (_listQueryLock)
            {
                var queue = new BlockingCollection<string>();
                _listItemsQueue = queue;

                sendMsg(new IpcMessage { Id = IpcMessageId.GetListItems, Hwnd = hwnd.ToInt64() });

                while (true)
                {
                    string? item;
                    if (queue.TryTake(out item, 2000))
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
                using var evt = new AutoResetEvent(false);
                _selectedIndicesEvent = evt;
                _selectedIndicesResult = null;

                sendMsg(new IpcMessage
                {
                    Id = IpcMessageId.GetSelectedIndices,
                    Hwnd = hwnd.ToInt64(),
                    StringVal1 = className
                });

                if (evt.WaitOne(1000))
                {
                    return _selectedIndicesResult ?? Array.Empty<int>();
                }
                return Array.Empty<int>();
            }
        }

        public static void AddListItemsChunk(string[]? chunk, bool isFinal)
        {
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

        public static void SetSelectedIndicesResult(int[]? result)
        {
            _selectedIndicesResult = result;
            try
            {
                _selectedIndicesEvent?.Set();
            }
            catch { }
        }
    }
}

