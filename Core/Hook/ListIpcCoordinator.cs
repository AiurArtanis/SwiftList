using System;
using System.Collections.Generic;
using System.Threading;

namespace SwiftList.Core.Hook
{
    public static class ListIpcCoordinator
    {
        private static readonly object _listQueryLock = new object();
        private static AutoResetEvent? _listItemsEvent;
        private static string[]? _listItemsResult;
        private static AutoResetEvent? _selectedIndicesEvent;
        private static int[]? _selectedIndicesResult;

        public static IEnumerable<string> GetListItems(IntPtr hwnd, Action<IpcMessage> sendMsg)
        {
            lock (_listQueryLock)
            {
                using var evt = new AutoResetEvent(false);
                _listItemsEvent = evt;
                _listItemsResult = null;

                sendMsg(new IpcMessage { Id = IpcMessageId.GetListItems, Hwnd = hwnd.ToInt64() });

                if (evt.WaitOne(1000))
                {
                    return _listItemsResult ?? Array.Empty<string>();
                }
                return Array.Empty<string>();
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

        public static void SetListItemsResult(string[]? result)
        {
            _listItemsResult = result;
            try
            {
                _listItemsEvent?.Set();
            }
            catch { }
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
