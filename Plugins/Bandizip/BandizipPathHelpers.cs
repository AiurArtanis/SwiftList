using System.IO;

namespace SwiftList.Plugins.Bandizip;

// Shared by BandizipExtractDialogAdapter and BandizipAddFilesDialogAdapter -- both dialogs' target fields
// are folder-only (see IFileDialogAdapter.TargetIsFolderOnly) and need the identical normalize/resolve
// logic, so it lives here once instead of copied per adapter.
internal static class BandizipPathHelpers
{
    // Pure normalize-and-check, pulled out so it's unit-testable without a live Bandizip window --
    // GetCurrentPath itself just supplies the live GetText()/Directory.Exists calls around it. Mirrors
    // WinRARExtractDialogAdapter.NormalizeIfExists's own strict "only if it actually exists" contract.
    public static string? NormalizeIfExists(string text, Func<string, bool> directoryExists)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.TrimEnd('\\', '/');
        return directoryExists(trimmed) ? trimmed : null;
    }

    // A destination-path field expects a folder -- a picked FILE needs to resolve to its containing
    // folder instead. Same reasoning and File.Exists-based discriminator as
    // WinRARExtractDialogAdapter.ResolveTargetFolder: Bandizip's own destination folder commonly doesn't
    // exist yet (it creates it on extract/compress), so "doesn't exist" must still be treated as "a folder
    // to create", not walked up a level as if it were a file's parent.
    public static string ResolveTargetFolder(string path) => File.Exists(path) ? (Path.GetDirectoryName(path) ?? path) : path;
}
