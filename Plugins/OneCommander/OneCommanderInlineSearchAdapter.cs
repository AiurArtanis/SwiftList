using System.IO;
using System.Runtime.InteropServices;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;
using SwiftList.Plugins.OneCommander.Automation;

namespace SwiftList.Plugins.OneCommander;

/// <summary>
/// Inline-search integration for OneCommander. Unlike Directory Opus / Total Commander (native apps with
/// distinct child HWNDs for their file list and address bar), OneCommander is a WPF host that renders
/// everything inside a single top-level HWND, so there is no native window-message protocol or reliable
/// child-class signal to key off. Reading/writing the current folder goes through UI Automation instead
/// (see Automation/UiaPathAccessor.cs).
/// </summary>
public class OneCommanderInlineSearchAdapter : IInlineSearchAdapter
{
    public string Name => "OneCommander";

    public bool IsFileExplorer => true;

    private const string ProcessNameValue = "OneCommander";

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (!PluginSettingsService.GetSetting("SwiftList.Plugins.OneCommander", "EnableInlineSearch", true))
            return false;

        if (string.IsNullOrEmpty(processName))
            return false;

        return processName.Equals(ProcessNameValue, StringComparison.OrdinalIgnoreCase);
    }

    // OneCommander renders its file list, address bar, and tabs all inside one native HWND -- there is no
    // distinct child window class to gate on the way TC's TMyListBox/LCLListBox or DO's dopus.filedisplay
    // let us tell "the file list has focus" apart from other controls, so the generic native
    // "is a text box focused" bypass elsewhere in the hook doesn't catch OneCommander's WPF text boxes
    // either. UiaFocusTracker checks the focused element's UIA ControlType instead (a semantic signal that
    // works regardless of native HWND structure), throttled so this per-keystroke hot path doesn't pay for
    // a UI Automation call on every character.
    public bool CanTrigger(IntPtr focusedHwnd, string className) =>
        focusedHwnd != IntPtr.Zero && !UiaFocusTracker.IsFocusedElementEditable();

    public bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor)
    {
        if (!PluginSettingsService.GetSetting("SwiftList.Plugins.OneCommander", "EnableQuickNav", true))
            return false;

        return CanTrigger(hwndUnderCursor, classNameUnderCursor);
    }

    public string? GetSearchScope(IntPtr hwnd)
    {
        // Called before the inline search window steals focus away, so this is also a good opportunity to
        // (re-)snapshot the active pane in case OneCommanderPathCollector didn't already (see UiaPathAccessor).
        UiaPathAccessor.RefreshFocusAnchor(hwnd);

        var path = UiaPathAccessor.GetCurrentPath(hwnd);
        return PathValidation.LooksLikeRootedPath(path) ? path : null;
    }

    public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
    {
        try
        {
            if (Directory.Exists(path))
            {
                return UiaPathAccessor.SetCurrentPath(hwnd, path);
            }
            if (File.Exists(path))
            {
                // UI Automation can't place the cursor on a specific file, so navigate to its folder.
                var parent = Path.GetDirectoryName(path);
                return !string.IsNullOrEmpty(parent) && UiaPathAccessor.SetCurrentPath(hwnd, parent);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero) return false;

        // Dock over the whole window's bottom-right corner (same fallback as the Directory Opus / Total
        // Commander plugins). Extended frame bounds excludes the drop shadow, matching the visible edge.
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var nativeRect, Marshal.SizeOf<RECT>()) == 0)
        {
            rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }
        if (GetWindowRect(hwnd, out nativeRect))
        {
            rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }
        return false;
    }

    public bool CanEnterActionsMode(IntPtr hwnd) => true;
}
