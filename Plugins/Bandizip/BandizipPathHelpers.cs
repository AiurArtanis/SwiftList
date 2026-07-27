namespace SwiftList.Plugins.Bandizip;

// Shared by BandizipExtractDialogAdapter and BandizipAddFilesDialogAdapter -- both need the identical
// normalize-and-check logic, so it lives here once instead of copied per adapter.
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
}
