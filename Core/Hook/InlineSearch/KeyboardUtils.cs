namespace SwiftList.Core.Hook.InlineSearch;

internal static class KeyboardUtils
{
    public static int GetKeyVirtualCode(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        key = key.Trim().ToUpperInvariant();
        if (key == "SPACE") return 0x20;
        if (key == "TAB") return 0x09;
        if (key == "ENTER" || key == "RETURN") return 0x0D;
        if (key == "ESC" || key == "ESCAPE") return 0x1B;
        if (key == "BACK" || key == "BACKSPACE") return 0x08;
        if (key == "CAPSLOCK") return 0x14;

        if (key.Length == 1 && key[0] >= 'A' && key[0] <= 'Z')
            return key[0];
        if (key.Length == 1 && key[0] >= '0' && key[0] <= '9')
            return key[0];

        if (key.StartsWith("F") && key.Length > 1 && int.TryParse(key.Substring(1), out var fNum) && fNum >= 1 && fNum <= 12)
        {
            return 0x6F + fNum; // F1 is 0x70, F12 is 0x7B
        }

        return 0;
    }

    public static bool CheckModifiersMatch(string expectedModifier)
    {
        var ctrlDown = (KeyboardNativeMethods.GetKeyState(0x11) & 0x8000) != 0;
        var altDown = (KeyboardNativeMethods.GetKeyState(0x12) & 0x8000) != 0;
        var shiftDown = (KeyboardNativeMethods.GetKeyState(0x10) & 0x8000) != 0;
        var winDown = (KeyboardNativeMethods.GetKeyState(0x5B) & 0x8000) != 0 ||
                       (KeyboardNativeMethods.GetKeyState(0x5C) & 0x8000) != 0;

        var expected = expectedModifier?.Trim().ToUpperInvariant() ?? "NONE";
        if (expected == "CONTROL" || expected == "CTRL")
            return ctrlDown && !altDown && !shiftDown && !winDown;
        if (expected == "ALT")
            return altDown && !ctrlDown && !shiftDown && !winDown;
        if (expected == "SHIFT")
            return shiftDown && !ctrlDown && !altDown && !winDown;
        if (expected == "WIN" || expected == "WINDOWS")
            return winDown && !ctrlDown && !altDown && !shiftDown;
        if (expected == "NONE")
            return !ctrlDown && !altDown && !shiftDown && !winDown;

        return false;
    }

    public static bool CheckModifiersMatchOnly(string expected)
    {
        var ctrlDown = (KeyboardNativeMethods.GetKeyState(0x11) & 0x8000) != 0;
        var altDown = (KeyboardNativeMethods.GetKeyState(0x12) & 0x8000) != 0;
        var shiftDown = (KeyboardNativeMethods.GetKeyState(0x10) & 0x8000) != 0;
        var winDown = (KeyboardNativeMethods.GetKeyState(0x5B) & 0x8000) != 0 ||
                       (KeyboardNativeMethods.GetKeyState(0x5C) & 0x8000) != 0;

        var exp = expected?.Trim().ToUpperInvariant() ?? "CONTROL";
        if (exp == "CONTROL" || exp == "CTRL")
            return ctrlDown && !altDown && !shiftDown && !winDown;
        if (exp == "ALT")
            return altDown && !ctrlDown && !shiftDown && !winDown;
        if (exp == "SHIFT")
            return shiftDown && !ctrlDown && !altDown && !winDown;
        if (exp == "WIN" || exp == "WINDOWS")
            return winDown && !ctrlDown && !altDown && !shiftDown;
        return false;
    }

    public static bool IsModifierKey(int vkCode, string modifier)
    {
        modifier = modifier?.Trim().ToUpperInvariant() ?? "CONTROL";
        if (modifier == "CONTROL" || modifier == "CTRL")
            return vkCode == 0x11 || vkCode == 0xA2 || vkCode == 0xA3;
        if (modifier == "ALT")
            return vkCode == 0x12 || vkCode == 0xA4 || vkCode == 0xA5;
        if (modifier == "SHIFT")
            return vkCode == 0x10 || vkCode == 0xA0 || vkCode == 0xA1;
        if (modifier == "WIN" || modifier == "WINDOWS")
            return vkCode == 0x5B || vkCode == 0x5C;
        return false;
    }

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
        var hProcess = KeyboardNativeMethods.OpenProcess(0x1000, false, processId); // 0x1000 = PROCESS_QUERY_LIMITED_INFORMATION
        if (hProcess != IntPtr.Zero)
        {
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                var size = (uint)sb.Capacity;
                if (KeyboardNativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                {
                    var fullPath = sb.ToString();
                    return Path.GetFileName(fullPath); // Returns name with extension, e.g. "cmd.exe"
                }
            }
            finally
            {
                KeyboardNativeMethods.CloseHandle(hProcess);
            }
        }

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

    public static string GetProcessNameWithoutExtension(uint processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }

    // True when the foreground window's IME is actively composing (open AND in native conversion mode),
    // for any IME language. Open status alone is unreliable: TSF IMEs (MS Pinyin, Rime, ...) report open
    // even in English mode, so their open status only means "IME on", not "composing". The conversion
    // mode's CMODE_NATIVE bit (set for Chinese/kana input, cleared for English/alphanumeric) is what
    // actually distinguishes it. Runs inside the low-level keyboard hook, so both queries use
    // SendMessageTimeout with a small timeout to never stall the callback.
    public static bool IsImeActive(IntPtr fgHwnd)
    {
        if (fgHwnd == IntPtr.Zero) return false;
        var hImeWnd = KeyboardNativeMethods.ImmGetDefaultIMEWnd(fgHwnd);
        if (hImeWnd == IntPtr.Zero) return false;

        // Must be open (IME turned on).
        if (KeyboardNativeMethods.SendMessageTimeout(hImeWnd, KeyboardNativeMethods.WM_IME_CONTROL,
                (IntPtr)KeyboardNativeMethods.IMC_GETOPENSTATUS, IntPtr.Zero,
                KeyboardNativeMethods.SMTO_ABORTIFHUNG, 40, out var open) == IntPtr.Zero || open == IntPtr.Zero)
            return false;

        // ...and in native (Chinese/kana) conversion mode. English/alphanumeric mode clears CMODE_NATIVE.
        // If the mode can't be read, fall back to the (conservative) open-status result.
        if (KeyboardNativeMethods.SendMessageTimeout(hImeWnd, KeyboardNativeMethods.WM_IME_CONTROL,
                (IntPtr)KeyboardNativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero,
                KeyboardNativeMethods.SMTO_ABORTIFHUNG, 40, out var conv) == IntPtr.Zero)
            return true;
        return (conv.ToInt64() & KeyboardNativeMethods.IME_CMODE_NATIVE) != 0;
    }

    public static char GetUnicodeChar(KeyboardNativeMethods.KBDLLHOOKSTRUCT hookStruct)
    {
        var keyboardState = new byte[256];
        KeyboardNativeMethods.GetKeyboardState(keyboardState);
        var sb = new System.Text.StringBuilder(2);
        var result = KeyboardNativeMethods.ToUnicode(hookStruct.vkCode, hookStruct.scanCode, keyboardState, sb, sb.Capacity, 0);
        if (result == 1 && !char.IsControl(sb[0]))
        {
            return sb[0];
        }
        return '\0';
    }
}
