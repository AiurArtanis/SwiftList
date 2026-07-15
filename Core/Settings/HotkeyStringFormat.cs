namespace SwiftList.Core;

/// <summary>
/// Shared parsing for the flat hotkey string format used by <see cref="HotkeyPageSettings.ToggleWindowHotkey"/>
/// and <see cref="HotkeyPageSettings.QuickSwitchHotkey"/>: a bare modifier token ("Ctrl"/"Alt"/"Shift"/"Win")
/// means double-tap that modifier; anything else ("Mod+Key" or a bare key) is a literal key combo.
/// </summary>
public static class HotkeyStringFormat
{
    private static readonly string[] ModifierTokens = { "Ctrl", "Alt", "Shift", "Win" };

    /// <summary>True if the value is a bare modifier (double-tap mode); <paramref name="modifier"/> is its
    /// canonical name ("Control"/"Alt"/"Shift"/"Win").</summary>
    public static bool IsBareModifier(string? value, out string modifier)
    {
        modifier = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !ModifierTokens.Contains(value, StringComparer.OrdinalIgnoreCase))
            return false;

        modifier = value.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ? "Control" : value;
        return true;
    }

    public static void ParseCombo(string? value, out string modifier, out string key)
    {
        if (string.IsNullOrWhiteSpace(value)) { modifier = string.Empty; key = string.Empty; return; }

        var parts = value.Split('+');
        if (parts.Length == 1)
        {
            // A single token is either a bare modifier alone (e.g. "Ctrl") or a bare key with no
            // modifier (e.g. "P") -- tell them apart instead of always assuming the latter.
            if (ModifierTokens.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
            {
                modifier = parts[0].Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ? "Control" : parts[0];
                key = string.Empty;
            }
            else
            {
                modifier = string.Empty;
                key = parts[0];
            }
            return;
        }

        key = parts[^1];
        var modPart = parts[0];
        modifier = modPart.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ? "Control" : modPart; // Win/Alt/Shift pass through
    }

    public static string FormatCombo(string modifier, string key)
    {
        var mod = modifier.Equals("Control", StringComparison.OrdinalIgnoreCase) ? "Ctrl" : modifier;
        if (string.IsNullOrEmpty(key)) return string.IsNullOrEmpty(mod) ? string.Empty : mod;
        return string.IsNullOrEmpty(mod) ? key : $"{mod}+{key}";
    }

    private static readonly Dictionary<string, string> OemDisplaySymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Oem1"] = ";", ["OemPlus"] = "=", ["OemComma"] = ",", ["OemMinus"] = "-", ["OemPeriod"] = ".",
        ["Oem2"] = "/", ["Oem3"] = "`", ["Oem4"] = "[", ["Oem5"] = "\\", ["Oem6"] = "]", ["Oem7"] = "'",
    };

    public static string ToDisplayText(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var parts = value.Split('+');
        if (OemDisplaySymbols.TryGetValue(parts[^1], out var symbol))
            parts[^1] = symbol;
        return string.Join("+", parts);
    }
}
