using System;

namespace SwiftList.PluginSdk
{
    /// <summary>
    /// Optional extension interface for IInlineSearchAdapter to support real-time 
    /// selection synchronization (following) when navigating results with arrow keys.
    /// </summary>
    public interface IInlineSearchSelectionFollower
    {
        /// <summary>
        /// Called when the selected item inside the inline search window shifts,
        /// letting the adapter instantly highlight/select the match in the host list.
        /// </summary>
        void OnSelectionChanged(IntPtr hwnd, string path);

        /// <summary>
        /// Called when the search session finishes (either confirmed/executed, or cancelled/closed).
        /// </summary>
        void OnSearchFinished(IntPtr hwnd, bool executed);
    }
}
