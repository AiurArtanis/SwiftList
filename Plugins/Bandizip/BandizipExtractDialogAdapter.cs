using System.IO;
using SwiftList.PluginSdk.Abstractions.Plugins.WindowAdapters;
using SwiftList.Plugins.Bandizip.Win32;

namespace SwiftList.Plugins.Bandizip;

/// <summary>
/// File-dialog integration for Bandizip's "选择解压路径" (choose extract path) dialog: lets SwiftList's own
/// path picker (the same one that drives native Open/Save dialogs -- see CoreExtensions' ClassicFileDialogAdapter
/// / FolderBrowserDialogAdapter, and WinRAR's own WinRARExtractDialogAdapter) fill in the destination path
/// field there. Detected purely by control structure (see BandizipDialogInterop.LooksLikeExtractDialog) --
/// Bandizip is localized, so nothing here reads window titles or control label text.
/// </summary>
public class BandizipExtractDialogAdapter : IFileDialogAdapter
{
    // Setting the path Edit's text and simulating the CBN_EDITCHANGE notification (see
    // BandizipDialogInterop.NotifyEditChanged) is what makes Bandizip's folder tree follow along -- but the
    // Windows Shell autocomplete popup attached to the same Edit reacts to that identical notification
    // asynchronously (confirmed live: scanning for it immediately after the SendMessage call sometimes
    // missed it entirely), not inline within the SendMessage call itself. This delay gives it time to
    // actually appear before SuppressAutoComplete goes looking for it.
    private const int AutoCompletePopupDelayMs = 150;

    public string Name => "Bandizip";

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName))
            return false;

        if (!processName.Equals("Bandizip", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!className.Equals("#32770", StringComparison.OrdinalIgnoreCase))
            return false;

        return BandizipDialogInterop.LooksLikeExtractDialog(hwnd);
    }

    // Pure normalize-and-check, pulled out so it's unit-testable without a live Bandizip window --
    // GetCurrentPath itself just supplies the live GetText()/Directory.Exists calls around it. Mirrors
    // WinRARExtractDialogAdapter.NormalizeIfExists's own strict "only if it actually exists" contract.
    internal static string? NormalizeIfExists(string text, Func<string, bool> directoryExists)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.TrimEnd('\\', '/');
        return directoryExists(trimmed) ? trimmed : null;
    }

    // Reads the ComboBox itself, not its child Edit -- same reasoning as WinRARExtractDialogAdapter:
    // the combo's own WM_GETTEXT reflects whatever it displays regardless of how that text got there,
    // where the child Edit's buffer alone has been observed empty in similar dialogs elsewhere in this
    // codebase.
    public string? GetCurrentPath(IntPtr hwnd)
    {
        var combo = BandizipDialogInterop.FindPathCombo(hwnd);
        return NormalizeIfExists(BandizipDialogInterop.GetText(combo), Directory.Exists);
    }

    // A destination-path field expects a folder -- a picked FILE needs to resolve to its containing
    // folder instead. Same reasoning and File.Exists-based discriminator as
    // WinRARExtractDialogAdapter.ResolveTargetFolder: Bandizip's own destination folder commonly doesn't
    // exist yet (it creates it on extract), so "doesn't exist" must still be treated as "a folder to
    // create", not walked up a level as if it were a file's parent.
    internal static string ResolveTargetFolder(string path) => File.Exists(path) ? (Path.GetDirectoryName(path) ?? path) : path;

    public bool NavigateTo(IntPtr hwnd, string targetPath)
    {
        var edit = BandizipDialogInterop.FindPathEdit(hwnd);
        var combo = BandizipDialogInterop.FindPathCombo(hwnd);
        if (edit == IntPtr.Zero || combo == IntPtr.Zero) return false;

        var folder = ResolveTargetFolder(targetPath);
        var result = BandizipDialogInterop.SetText(edit, folder);
        if (!result) return false;

        BandizipDialogInterop.NotifyEditChanged(combo);
        Thread.Sleep(AutoCompletePopupDelayMs);
        BandizipDialogInterop.SuppressAutoComplete(hwnd);
        return true;
    }

    // Returns the whole dialog's bounds, not just the (single-line, short) path ComboBox's own rect: the
    // host's own docking logic rejects any rect under 100px tall as "not a real target" and falls back to
    // a fixed bottom-right-of-screen position -- see InlineSearchWindowPositioner.PositionWindowCore and
    // the identical reasoning already documented on WinRARExtractDialogAdapter.GetDockBounds.
    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero || !BandizipDialogInterop.TryGetDialogRect(hwnd, out var r))
            return false;

        rect = new AdapterRect { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
        return true;
    }

    public bool RestoreFocus(IntPtr hwnd)
    {
        var edit = BandizipDialogInterop.FindPathEdit(hwnd);
        return edit != IntPtr.Zero && BandizipDialogInterop.SetForegroundAndFocus(hwnd, edit);
    }
}
