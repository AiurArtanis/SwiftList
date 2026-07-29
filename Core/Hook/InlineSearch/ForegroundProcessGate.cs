namespace SwiftList.Core.Hook.InlineSearch;

// Checks the foreground window's owning process against the user's blacklist -- used to suppress
// inline-search/quick-nav hotkeys while a blacklisted app (e.g. a game) is focused. Kept separate from
// KeyboardUtils: process identification has nothing to do with key/modifier translation.
internal static class ForegroundProcessGate
{
    public static bool IsForegroundProcessBlacklisted(List<string> blacklistedProcesses)
    {
        if (blacklistedProcesses == null || blacklistedProcesses.Count == 0)
            return false;

        try
        {
            var fgHwnd = KeyboardNativeMethods.GetForegroundWindow();
            if (fgHwnd == IntPtr.Zero) return false;
            KeyboardNativeMethods.GetWindowThreadProcessId(fgHwnd, out var processId);
            if (processId == 0) return false;

            var procName = GetProcessNameById(processId);
            if (string.IsNullOrEmpty(procName)) return false;

            foreach (var blacklisted in blacklistedProcesses)
            {
                if (string.IsNullOrEmpty(blacklisted)) continue;
                if (blacklisted.Equals(procName, StringComparison.OrdinalIgnoreCase) ||
                    blacklisted.Equals(procName + ".exe", StringComparison.OrdinalIgnoreCase) ||
                    (procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                     blacklisted.Equals(procName.Substring(0, procName.Length - 4), StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        return false;
    }

    private static string? GetProcessNameById(uint processId)
    {
        if (ProcessNameResolver.TryGetImagePath(processId, out var fullPath))
            return Path.GetFileName(fullPath); // Returns name with extension, e.g. "cmd.exe"

        // Fallback to .NET Process class (in case OpenProcess fails or limited info is not available, though unlikely)
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName; // Returns name without extension, e.g. "cmd"
        }
        catch
        {
            return null;
        }
    }

    // Called for every keystroke the inline-search hook sees, so it goes through ProcessNameResolver
    // rather than Process.GetProcessById -- a low-level keyboard hook that takes too long is dropped by
    // Windows outright.
    public static string GetProcessNameWithoutExtension(uint processId) =>
        ProcessNameResolver.GetNameWithoutExtension(processId);
}
