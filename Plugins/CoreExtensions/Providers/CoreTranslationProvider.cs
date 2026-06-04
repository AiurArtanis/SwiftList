using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.CoreExtensions.Providers
{
    /// <summary>
    /// Core implementation of the translation provider, managing Chinese and English localized resources.
    /// Loaded dynamically from nested folder structures Resources/Translations/{lang}/{type}.json
    /// </summary>
    public class CoreTranslationProvider : ITranslationProvider
    {
        public string Name => "Core Translation Provider";

        public IReadOnlyList<string> SupportedCultures
        {
            get
            {
                var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var assembly = Assembly.GetExecutingAssembly();
                try
                {
                    string prefix = "Resources.Translations.";
                    var resourceNames = assembly.GetManifestResourceNames();
                    foreach (var name in resourceNames)
                    {
                        int index = name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                        if (index >= 0)
                        {
                            string sub = name.Substring(index + prefix.Length);
                            int nextDot = sub.IndexOf('.');
                            if (nextDot > 0)
                            {
                                string cultureKey = sub.Substring(0, nextDot).Replace('_', '-');
                                // Ensure standard ISO format or valid culture format
                                if (cultureKey.Contains("-") && cultureKey.Length >= 5)
                                {
                                    cultures.Add(cultureKey);
                                }
                            }
                        }
                    }
                }
                catch { }

                if (cultures.Count == 0)
                {
                    return new[] { "zh-CN", "en-US" };
                }

                return cultures.OrderBy(c => c == "zh-CN" ? 0 : c == "en-US" ? 1 : 2).ThenBy(c => c).ToList();
            }
        }

        private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object LockObj = new();

        public IReadOnlyDictionary<string, string> GetTranslations(string cultureName)
        {
            lock (LockObj)
            {
                if (Cache.TryGetValue(cultureName, out var cached))
                {
                    return cached;
                }

                var translations = LoadMergedTranslations(cultureName);
                Cache[cultureName] = translations;
                return translations;
            }
        }

        private static Dictionary<string, string> LoadMergedTranslations(string cultureKey)
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1. Load main app translations
            LoadTranslationsInto(merged, cultureKey, "App");

            // 2. Load plugin-specific translations (will override app translations if keys clash)
            LoadTranslationsInto(merged, cultureKey, "Plugin");

            return merged;
        }

        private static void LoadTranslationsInto(Dictionary<string, string> target, string cultureKey, string typeName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string cultureKeyUnderscore = cultureKey.Replace('-', '_');
            
            // Find resource names ending with the required suffix to avoid rigid hardcoded assembly prefixes
            string suffix1 = $"{cultureKey}.{typeName}.json";
            string suffix2 = $"{cultureKeyUnderscore}.{typeName}.json";

            string? matchedResourceName = null;
            try
            {
                var resourceNames = assembly.GetManifestResourceNames();
                foreach (var name in resourceNames)
                {
                    if (name.EndsWith(suffix1, StringComparison.OrdinalIgnoreCase) || 
                        name.EndsWith(suffix2, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedResourceName = name;
                        break;
                    }
                }
            }
            catch
            {
                // Fallback to strict guess if reflection fails
                matchedResourceName = $"CoreExtensions.Resources.Translations.{cultureKeyUnderscore}.{typeName}.json";
            }

            if (string.IsNullOrEmpty(matchedResourceName)) return;

            Stream? stream = assembly.GetManifestResourceStream(matchedResourceName);
            if (stream == null) return;

            using (stream)
            using (StreamReader reader = new StreamReader(stream))
            {
                string json = reader.ReadToEnd();
                try
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        foreach (var kvp in dict)
                        {
                            target[kvp.Key] = kvp.Value;
                        }
                    }
                }
                catch
                {
                    // Ignore parsing exceptions to maintain runtime robustness
                }
            }
        }
    }
}
