using System.IO;
using SwiftList.PluginSdk.Abstractions.Plugins.WindowAdapters;
using SwiftList.Plugins.WinRAR.Win32;

namespace SwiftList.Plugins.WinRAR;

/// <summary>
/// File-dialog integration for WinRAR's "Extract path and options" dialog: lets SwiftList's own path
/// picker (the same one that drives native Open/Save dialogs -- see CoreExtensions' ClassicFileDialogAdapter
/// / FolderBrowserDialogAdapter) fill in the destination path field there. Detected purely by control
/// structure (see WinRARDialogInterop.LooksLikeExtractDialog) -- WinRAR is localized, so nothing here reads
/// window titles or control label text.
/// </summary>
public class WinRARExtractDialogAdapter : IFileDialogAdapter
{
    public string Name => "WinRAR";

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName))
            return false;

        if (!processName.Equals("WinRAR", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!className.Equals("#32770", StringComparison.OrdinalIgnoreCase))
            return false;

        return WinRARDialogInterop.LooksLikeExtractDialog(hwnd);
    }

    // Pure normalize-and-check, pulled out so it's unit-testable without a live WinRAR window --
    // GetCurrentPath itself just supplies the live GetText()/Directory.Exists calls around it. Mirrors
    // ClassicFileDialogAdapter.GetCurrentPath's own strict "only if it actually exists" contract, unlike
    // IInlineSearchAdapter.GetSearchScope's looser ancestor-walking fallback used elsewhere in this repo --
    // different interface, different contract.
    internal static string? NormalizeIfExists(string text, Func<string, bool> directoryExists)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.TrimEnd('\\', '/');
        return directoryExists(trimmed) ? trimmed : null;
    }

    // Reads the ComboBox itself, not its child Edit: confirmed empirically that when the dialog's
    // displayed path comes from the combo's own current selection (e.g. its default on a freshly opened
    // dialog) rather than something the user actually typed, WM_GETTEXT on the child Edit alone returns
    // empty even though the combo visibly shows a real path. The combo's own WM_GETTEXT reflects
    // whatever it displays either way. (WinRARDialogInterop.GetText's own SendMessageTimeout separately
    // fixed an intermittent empty-read regardless of which control this queries.)
    public string? GetCurrentPath(IntPtr hwnd)
    {
        var combo = WinRARDialogInterop.FindPathCombo(hwnd);
        return NormalizeIfExists(WinRARDialogInterop.GetText(combo), Directory.Exists);
    }

    // A destination-path field expects a folder -- a picked FILE needs to resolve to its containing
    // folder instead. QuickNavigationNavigator already does this itself before ever calling NavigateTo
    // (isDir ? path : Path.GetDirectoryName(path)), but the quick/inline search bar's own fallback chain
    // (InlineSearchNavigator.RunFallbackChain) does not -- it hands the raw picked path straight through.
    // Resolving it here too means NavigateTo is correct regardless of which caller reaches it, rather than
    // depending on every future caller remembering to pre-resolve. File.Exists (not Directory.Exists) is
    // the discriminator: WinRAR's own destination folder commonly doesn't exist yet (it creates it on
    // extract, per the dialog's own hint text), so "doesn't exist" must still be treated as "a folder to
    // create", not walked up a level as if it were a file.
    internal static string ResolveTargetFolder(string path) => File.Exists(path) ? (Path.GetDirectoryName(path) ?? path) : path;

    public bool NavigateTo(IntPtr hwnd, string targetPath)
    {
        var edit = WinRARDialogInterop.FindPathEdit(hwnd);
        if (edit == IntPtr.Zero) return false;

        var folder = ResolveTargetFolder(targetPath);
        var result = WinRARDialogInterop.SetText(edit, folder);
        if (result)
            WinRARDialogInterop.ClickDisplayButton(hwnd);
        return result;
    }

    // Returns the whole dialog's bounds, not just the (single-line, ~27px-tall) path ComboBox's own rect:
    // the host's own docking logic rejects any rect under 100px tall as "not a real target" (see
    // InlineSearchWindowPositioner.PositionWindowCore's hasValidRect check) and falls back to a
    // fixed bottom-right-of-screen position -- confirmed empirically the ComboBox's rect alone was silently
    // hitting exactly that fallback. Same whole-dialog convention ClassicFileDialogAdapter/
    // FolderBrowserDialogAdapter already use for Explorer's own common dialogs.
    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero || !WinRARDialogInterop.TryGetDialogRect(hwnd, out var r))
            return false;

        rect = new AdapterRect { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
        return true;
    }

    public bool RestoreFocus(IntPtr hwnd)
    {
        var edit = WinRARDialogInterop.FindPathEdit(hwnd);
        return edit != IntPtr.Zero && WinRARDialogInterop.SetForegroundAndFocus(hwnd, edit);
    }
}
