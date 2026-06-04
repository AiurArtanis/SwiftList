using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SwiftList.Core
{
    public class UserSettings
    {
        public List<NetworkDriveSetting> NetworkDrives { get; set; } = new();
        public List<string> ExcludedPaths { get; set; } = new();
        public List<string> IgnoredPathGlobs { get; set; } = new();
        public List<string> IgnoredPathRegexes { get; set; } = new();
        public bool StartWithWindows { get; set; }
        public bool AutoElevateIfAdmin { get; set; }
        public string LogLevel { get; set; } = "Info";
        public string PreferredLanguage { get; set; } = GetDefaultSystemLanguage();
        public string Theme { get; set; } = "Light";

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

        private static UserSettings LoadFromDisk()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new UserSettings();

                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
            }
            catch (Exception ex)
            {
                Logger.Log($"[UserSettings] Failed to load settings: {ex.Message}");
                return new UserSettings();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Logger.UserDataDir);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, options));
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
}
