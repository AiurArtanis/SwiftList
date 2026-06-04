using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SwiftList.PluginSdk;

namespace SwiftList.Core
{
    public static class AliasProviderRegistry
    {
        private static readonly ConcurrentBag<IAliasProvider> Providers = new();

        public static Func<IAliasProvider, bool> FilterFunc { get; set; } = _ => true;

        public static void Register(IAliasProvider provider)
        {
            if (provider == null) return;
            Providers.Add(provider);
            Logger.Log($"[AliasProviderRegistry] Registered alias provider: {provider.Name} ({provider.Id})");
        }

        public static IEnumerable<IAliasProvider> GetActiveProviders()
        {
            foreach (var prov in Providers)
            {
                if (FilterFunc(prov))
                {
                    yield return prov;
                }
            }
        }

        /// <summary>
        /// Returns ALL registered alias providers, regardless of the enabled/disabled filter.
        /// Used by the settings UI to show unchecked (disabled) providers instead of hiding them.
        /// </summary>
        public static IEnumerable<IAliasProvider> GetAllProviders() => Providers;


        /// <summary>
        /// A highly optimized helper to detect if a string contains non-ASCII characters.
        /// ASCII characters are in the range [0, 127].
        /// </summary>
        public static bool HasNonAscii(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] > 127)
                    return true;
            }

            return false;
        }
    }
}
