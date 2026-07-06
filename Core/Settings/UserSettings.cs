using System.Text.Json;

namespace SwiftList.Core;

public class UserSettings
{
    public List<NetworkDriveSetting> NetworkDrives { get; set; } = new();
    public List<WslSetting> WslSettings { get; set; } = new();
    public List<FavoriteItemSetting> Favorites { get; set; } = new();
    public List<string> ExcludedPaths { get; set; } = new()
    {
        "%SystemDrive%\\Windows.old",
        "%ProgramData%",
        "%SystemRoot%",
        "%ProgramW6432%",
        "%USERPROFILE%\\AppData",
        "%ProgramFiles(x86)%"
    };
    public List<string> IgnoredPathGlobs { get; set; } = new()
    {
        ".*",
        "~*",
        "\\$*",
        "node_modules"
    };
    public List<string> IgnoredPathRegexes { get; set; } = new();
    public List<string> BlacklistedProcesses { get; set; } = new();
    public bool EnableHistory { get; set; } = true;
    public bool EnableKeywordHistory { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool AutoElevateIfAdmin { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public bool AutoSilentUpdate { get; set; } = false;
    public string LogLevel { get; set; } = "Info";
    public string PreferredLanguage { get; set; } = GetDefaultSystemLanguage();
    public string Theme { get; set; } = "Light";
    public HotkeyPageSettings Hotkeys { get; set; } = new();
    public SearchWindowSettings SearchWindow { get; set; } = new();
    public PreviewWindowSettings PreviewWindow { get; set; } = new();

    private static string GetDefaultSystemLanguage()
    {
        try
        {
            return System.Globalization.CultureInfo.CurrentUICulture.Name;
        }
        catch
        {
            return "en-US";
        }
    }

    /// <summary>
    /// Stores IDs of disabled plugin sub-components (actions, dynamic providers, instant providers, filter providers, column providers).
    /// Format: "{PluginDllFileName}::{ComponentType}::{ComponentName}"
    /// </summary>
    public List<string> DisabledPluginComponents { get; set; } = new();

    public Dictionary<string, Dictionary<string, object>> PluginSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public T GetPluginSetting<T>(string pluginId, string key, T defaultValue)
    {
        if (PluginSettings.TryGetValue(pluginId, out var settingsDict) && settingsDict.TryGetValue(key, out var val))
        {
            try
            {
                if (val is T typedVal)
                {
                    return typedVal;
                }
                if (val is JsonElement element)
                {
                    return JsonSerializer.Deserialize<T>(element.GetRawText())!;
                }
                return (T)Convert.ChangeType(val, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }

    public void SetPluginSetting(string pluginId, string key, object? value)
    {
        if (!PluginSettings.TryGetValue(pluginId, out var settingsDict))
        {
            settingsDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            PluginSettings[pluginId] = settingsDict;
        }
        if (value == null)
        {
            settingsDict.Remove(key);
        }
        else
        {
            settingsDict[key] = value;
        }
    }

    public static string SettingsPath => Path.Combine(Logger.UserDataDir, "user-settings.json");

    private static UserSettings? _cachedSettings;
    private static string? _lastJsonOnDisk;
    private static readonly object _cacheLock = new();

    public static UserSettings Load()
    {
        lock (_cacheLock)
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            _cachedSettings = LoadFromDisk();
            return _cachedSettings;
        }
    }

    public static UserSettings ForceReload()
    {
        lock (_cacheLock)
        {
            _cachedSettings = LoadFromDisk();
            return _cachedSettings;
        }
    }

    private static UserSettings LoadFromDisk()
    {
        var retries = 5;
        while (retries > 0)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new UserSettings();

                using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                lock (_cacheLock)
                {
                    _lastJsonOnDisk = json;
                }
                return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
            }
            catch (IOException)
            {
                retries--;
                if (retries <= 0) throw;
                Task.Delay(50).Wait();
            }
            catch (Exception ex)
            {
                Logger.Log($"[UserSettings] Failed to load settings: {ex.Message}", Core.LogLevel.Warn);
                return new UserSettings();
            }
        }
        return new UserSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Logger.UserDataDir);
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(this, options);

        lock (_cacheLock)
        {
            if (json == _lastJsonOnDisk)
            {
                _cachedSettings = this;
                return;
            }
        }

        var retries = 5;
        while (retries > 0)
        {
            try
            {
                using var stream = new FileStream(SettingsPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream);
                writer.Write(json);
                break;
            }
            catch (IOException)
            {
                retries--;
                if (retries <= 0) throw;
                Task.Delay(50).Wait();
            }
        }

        lock (_cacheLock)
        {
            _cachedSettings = this;
            _lastJsonOnDisk = json;
        }
        ExclusionRuleSet.InvalidateCache();
    }
}

public class NetworkDriveSetting
{
    public string Id { get; set; } = string.Empty;
    public string RefreshMode { get; set; } = "Manual";
}

public class WslSetting
{
    public string Id { get; set; } = string.Empty; // e.g. "Ubuntu"
    public string RefreshMode { get; set; } = "Manual";
}

/// <summary>Everything shown on the Hotkey Settings page, grouped under one object.</summary>
public class HotkeyPageSettings
{
    /// <summary>
    /// A bare modifier (e.g. "Ctrl") means double-tap that modifier; a combo (e.g. "Alt+Space") means a
    /// literal key combination. See <see cref="HotkeyStringFormat"/>.
    /// </summary>
    public string ToggleWindowHotkey { get; set; } = "Ctrl";

    /// <summary>Same flat format as <see cref="ToggleWindowHotkey"/>.</summary>
    public string QuickSwitchHotkey { get; set; } = "Ctrl+G";

    public string SelectJumpModifier { get; set; } = "Ctrl";
    public string NextItemHotkey { get; set; } = "Ctrl+N";
    public string PreviousItemHotkey { get; set; } = "Ctrl+P";
    public string ActionsMenuHotkey { get; set; } = "Ctrl+O";
    public string CompleteFromSelectionHotkey { get; set; } = "Ctrl+Tab";
    public string QuickLookHotkey { get; set; } = "Alt+P";
    public bool QuickNavTriggerOnDoubleClick { get; set; } = true;
    public bool QuickNavTriggerOnMiddleClick { get; set; } = true;

    // Cycle back/forward through KeywordHistoryStore entries in the quick window's search box.
    public string KeywordHistoryPreviousHotkey { get; set; } = "Alt+Up";
    public string KeywordHistoryNextHotkey { get; set; } = "Alt+Down";

    /// <summary>
    /// User overrides for plugin action hotkeys, keyed by plugin ID (the DLL file name without its
    /// extension, matching <see cref="PluginSettings"/>'s convention) then by
    /// <c>ISearchResultAction.Id</c>. An empty string value means the action's hotkey is explicitly
    /// disabled; a missing entry (either level) means "use the action's own built-in default".
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> PluginActionHotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class FavoriteItemSetting
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public class SearchWindowSettings
{
    public double SearchBarWidth { get; set; } = 632;
    public double SearchBarHeight { get; set; } = 70;
    public double CornerRadius { get; set; } = 12;
    // Base result-icon size for the quick window only (see UiMetrics); other windows use a fixed size.
    public double ResultIconSize { get; set; } = 42;
    public double? Left { get; set; }
    public double? Top { get; set; }
}

public class PreviewWindowSettings
{
    // Defaults match the default search bar height (70) plus a fully-expanded 9-item results list
    // (9 * BaseSearchResultItemHeight = 459) -- see UiMetrics -- so the preview window's height is
    // predictable and doesn't change with however many results happen to be showing right now.
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 529;
}

