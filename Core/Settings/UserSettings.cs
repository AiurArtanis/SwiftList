using System.Text.Json;

namespace SwiftList.Core;

public class UserSettings
{
    public List<NetworkDriveSetting> NetworkDrives { get; set; } = new();
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
    public bool StartWithWindows { get; set; }
    public bool AutoElevateIfAdmin { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public bool AutoSilentUpdate { get; set; } = false;
    public bool QuickNavTriggerOnDoubleClick { get; set; } = true;
    public bool QuickNavTriggerOnMiddleClick { get; set; } = true;
    public string LogLevel { get; set; } = "Info";
    public string PreferredLanguage { get; set; } = GetDefaultSystemLanguage();
    public string Theme { get; set; } = "Light";
    public HotkeySetting ToggleWindowHotkey { get; set; } = new()
    {
        Type = "ModifierClick",
        Modifier = "Control",
        Key = "Space",
        ClickModifier = "Control",
        ClickCount = 2
    };
    public HotkeySetting QuickSwitchHotkey { get; set; } = new()
    {
        Type = "KeyCombo",
        Modifier = "Control",
        Key = "G",
        ClickModifier = "Control",
        ClickCount = 2
    };
    public string SelectIndexModifier { get; set; } = "Control";

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

public class HotkeySetting
{
    // Type: "KeyCombo" or "ModifierClick"
    public string Type { get; set; } = "KeyCombo";

    // For KeyCombo: "Control", "Alt", "Shift", "Win", "None"
    public string Modifier { get; set; } = "None";

    // For KeyCombo: "A"-"Z", "0"-"9", "Space", "F1"-"F12", "Tab", "Enter", "Escape"
    public string Key { get; set; } = "None";

    // For ModifierClick: "Control", "Alt", "Shift", "Win"
    public string ClickModifier { get; set; } = "Control";

    // For ModifierClick: number of clicks
    public int ClickCount { get; set; } = 2;
}

public class FavoriteItemSetting
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

