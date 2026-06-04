using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SwiftList.Core
{
    public class UserSettings
    {
        public List<NetworkDriveSetting> NetworkDrives { get; set; } = new();
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
        public bool StartWithWindows { get; set; }
        public bool AutoElevateIfAdmin { get; set; }
        public bool AutoCheckUpdates { get; set; } = true;
        public bool AutoSilentUpdate { get; set; } = false;
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

        public static string SettingsPath => Path.Combine(Logger.UserDataDir, "user-settings.json");

        private static UserSettings? _cachedSettings;
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
            int retries = 5;
            while (retries > 0)
            {
                try
                {
                    if (!File.Exists(SettingsPath))
                        return new UserSettings();

                    using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    string json = reader.ReadToEnd();
                    return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                }
                catch (IOException)
                {
                    retries--;
                    if (retries <= 0) throw;
                    System.Threading.Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[UserSettings] Failed to load settings: {ex.Message}", SwiftList.Core.LogLevel.Warn);
                    return new UserSettings();
                }
            }
            return new UserSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Logger.UserDataDir);
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(this, options);

            int retries = 5;
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
                    System.Threading.Thread.Sleep(50);
                }
            }

            lock (_cacheLock)
            {
                _cachedSettings = this;
            }
            ExclusionRuleSet.InvalidateCache();
        }
    }

    public class NetworkDriveSetting
    {
        public string Drive { get; set; } = string.Empty;
        public bool Enabled { get; set; }
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
}
