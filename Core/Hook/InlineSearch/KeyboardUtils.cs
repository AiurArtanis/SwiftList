using System;
using System.Collections.Generic;

namespace SwiftList.Core.Hook.InlineSearch
{
    public static class KeyboardUtils
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
                
            if (key.StartsWith("F") && key.Length > 1 && int.TryParse(key.Substring(1), out int fNum) && fNum >= 1 && fNum <= 12)
            {
                return 0x6F + fNum; // F1 is 0x70, F12 is 0x7B
            }
            
            return 0;
        }

        public static bool CheckModifiersMatch(string expectedModifier)
        {
            bool ctrlDown = (KeyboardNativeMethods.GetKeyState(0x11) & 0x8000) != 0;
            bool altDown = (KeyboardNativeMethods.GetKeyState(0x12) & 0x8000) != 0;
            bool shiftDown = (KeyboardNativeMethods.GetKeyState(0x10) & 0x8000) != 0;
            bool winDown = (KeyboardNativeMethods.GetKeyState(0x5B) & 0x8000) != 0 || 
                           (KeyboardNativeMethods.GetKeyState(0x5C) & 0x8000) != 0;

            string expected = expectedModifier?.Trim().ToUpperInvariant() ?? "NONE";
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
            bool ctrlDown = (KeyboardNativeMethods.GetKeyState(0x11) & 0x8000) != 0;
            bool altDown = (KeyboardNativeMethods.GetKeyState(0x12) & 0x8000) != 0;
            bool shiftDown = (KeyboardNativeMethods.GetKeyState(0x10) & 0x8000) != 0;
            bool winDown = (KeyboardNativeMethods.GetKeyState(0x5B) & 0x8000) != 0 || 
                           (KeyboardNativeMethods.GetKeyState(0x5C) & 0x8000) != 0;

            string exp = expected?.Trim().ToUpperInvariant() ?? "CONTROL";
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
                IntPtr fgHwnd = KeyboardNativeMethods.GetForegroundWindow();
                if (fgHwnd == IntPtr.Zero) return false;
                KeyboardNativeMethods.GetWindowThreadProcessId(fgHwnd, out uint processId);
                if (processId == 0) return false;
                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                string procName = process.ProcessName;
                foreach (var blacklisted in blacklistedProcesses)
                {
                    if (string.IsNullOrEmpty(blacklisted)) continue;
                    if (blacklisted.Equals(procName, StringComparison.OrdinalIgnoreCase) ||
                        blacklisted.Equals(procName + ".exe", StringComparison.OrdinalIgnoreCase))
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
    }
}
