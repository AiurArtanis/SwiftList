using System;
using System.Collections.Generic;

namespace SwiftList.PluginSdk
{
    /// <summary>
    /// Optional extension interface for IInlineSearchAdapter to support custom item list querying
    /// inside non-explorer controls (e.g. standard ListBox or SysListView32 controls).
    /// </summary>
    public interface IInlineSearchListProvider
    {
        /// <summary>
        /// Retrieves the full array of text items currently visible/available in the target control.
        /// </summary>
        IEnumerable<string> GetListItems(IntPtr hwnd);
    }
}
