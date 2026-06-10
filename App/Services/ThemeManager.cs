using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using SwiftList.Core;

namespace SwiftList.App.Services
{
    public class ThemeManager
    {
        private static readonly Lazy<ThemeManager> _instance = new(() => new ThemeManager());
        public static ThemeManager Instance => _instance.Value;

        private ResourceDictionary? _activeThemeDictionary;
        private string _currentThemeId = "Light";

        public string CurrentThemeId => _currentThemeId;

        private ThemeManager()
        {
        }

        public IEnumerable<SwiftList.PluginSdk.ITheme> GetAvailableThemes()
        {
            return PluginManager.Instance.ThemeProviders
                .SelectMany(p => p.GetThemes())
                .GroupBy(t => t.Id)
                .Select(g => g.First()); // Avoid duplicates
        }

        public void Initialize(string preferredThemeId)
        {
            ApplyTheme(preferredThemeId, saveSettings: false);
        }

        public bool ApplyTheme(string themeId, bool saveSettings = true)
        {
            var themes = GetAvailableThemes().ToList();
            var theme = themes.FirstOrDefault(t => string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase));

            // Fallback to Light if not found
            if (theme == null)
            {
                theme = themes.FirstOrDefault(t => string.Equals(t.Id, "Light", StringComparison.OrdinalIgnoreCase));
            }

            if (theme == null)
            {
                Logger.Log($"[ThemeManager] No themes found, failed to apply theme '{themeId}'", SwiftList.Core.LogLevel.Error);
                return false;
            }

            try
            {
                var newDict = theme.GetResources();

                // Apply to application-level resources
                var appResources = System.Windows.Application.Current.Resources;

                if (_activeThemeDictionary != null)
                {
                    appResources.MergedDictionaries.Remove(_activeThemeDictionary);
                }

                appResources.MergedDictionaries.Add(newDict);
                _activeThemeDictionary = newDict;
                _currentThemeId = theme.Id;

                Logger.Log($"[ThemeManager] Theme applied successfully: '{theme.DisplayName}' (Dark: {theme.IsDark})");

                if (saveSettings)
                {
                    var settings = UserSettings.Load();
                    settings.Theme = _currentThemeId;
                    settings.Save();
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"[ThemeManager] Error applying theme '{themeId}': {ex.Message}", SwiftList.Core.LogLevel.Error);
                return false;
            }
        }
    }
}
