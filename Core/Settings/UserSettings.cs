using System.Text.Json;

namespace SwiftList.Core;

public class UserSettings
{
    public List<NetworkDriveSetting> NetworkDrives { get; set; } = new();
    public List<WslSetting> WslSettings { get; set; } = new();
    public List<FolderIndexSetting> FolderIndexes { get; set; } = new();
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
    public bool StartWithWindows { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    public bool AutoSilentUpdate { get; set; } = false;
    // Applied only to QuickSearchWindow (Window_Loaded), not process-wide -- see GitHub issue #82
    // (NVIDIA Advanced Optimus GPU hot-switch blocked by this window's persistent DirectX composition
    // surface, since it's created once at startup and only ever hidden, never closed). Requires a
    // restart to take effect, since the window's HwndTarget.RenderMode is only set once at load.
    public bool EnableHardwareAcceleration { get; set; } = true;
    // The Quick window's tray-menu capsule button (only shown while this is true) is the replacement
    // entry point for Settings/Exit/etc., so hiding the tray icon never strands the user -- see
    // QuickSearchWindow's BtnTrayMenu and TrayIconService.ShowMenuAt.
    public bool HideTrayIcon { get; set; } = false;
    public string LogLevel { get; set; } = "Info";
    public string PreferredLanguage { get; set; } = GetDefaultSystemLanguage();
    public string Theme { get; set; } = "Light";
    public bool ThemeFollowSystem { get; set; } = false;
    // Empty means "unset" -- themes come entirely from plugins, so there's no safe hardcoded default
    // here; ThemeManager.ResolveLightDarkThemeId falls back to whatever theme is first available.
    public string LightThemeId { get; set; } = string.Empty;
    public string DarkThemeId { get; set; } = string.Empty;
    public HotkeyPageSettings Hotkeys { get; set; } = new();
    public SearchWindowSettings SearchWindow { get; set; } = new();
    public PreviewWindowSettings PreviewWindow { get; set; } = new();
    public MainWindowSettings MainWindow { get; set; } = new();
    public StartupPanelSettings StartupPanel { get; set; } = new();

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

    /// <summary>
    /// User-chosen display order for IQuickNavigationProvider entries in the quick-navigation menu's
    /// root level, most-preferred first. Same id format as DisabledPluginComponents. A provider whose
    /// id isn't present here yet (newly installed, or never reordered) falls back to its original
    /// discovery order, appended after every listed provider -- see PluginManager.QuickNavigationProviders.
    /// </summary>
    public List<string> QuickNavigationProviderOrder { get; set; } = new();

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

        PluginSdk.Services.PluginSettingsService.NotifySettingChanged(pluginId, key);
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

