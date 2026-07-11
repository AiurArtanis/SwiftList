using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.Core;

public static class AliasProviderRegistry
{
    private static readonly ConcurrentBag<IAliasProvider> Providers = new();
    private static readonly ConcurrentDictionary<string, byte> ProviderIdMap = new(StringComparer.OrdinalIgnoreCase);
    private static byte _nextId = 0;

    public static Func<IAliasProvider, bool> FilterFunc { get; set; } = _ => true;

    public static void Register(IAliasProvider provider)
    {
        if (provider == null) return;
        Providers.Add(provider);

        var componentId = GetComponentId(provider);
        var id = ProviderIdMap.GetOrAdd(componentId, _ => _nextId++);
        Logger.Log($"[AliasProviderRegistry] Registered alias provider: {provider.Name} with ID: {id} ({componentId})");
    }

    public static byte GetProviderId(IAliasProvider provider)
        => ProviderIdMap.TryGetValue(GetComponentId(provider), out var id) ? id : (byte)0;

    public static byte GetProviderIdByComponentId(string componentId) => ProviderIdMap.TryGetValue(componentId, out var id) ? id : (byte)255; // 255 represents not found

    private static string GetComponentId(IAliasProvider provider)
    {
        var dllName = Path.GetFileName(provider.GetType().Assembly.Location);
        var typeName = provider.GetType().Name;
        return $"{dllName}::AliasProvider::{typeName}";
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
    /// Used by the settings UI to show unchecked (disabled) providers instead of hiding them, and by
    /// AliasGeneration -- a provider that's merely disabled must still have its aliases baked in, so
    /// re-enabling it later is a free query-time flip instead of needing a rebuild (see
    /// ComputeProvidersFingerprint).
    /// </summary>
    public static IEnumerable<IAliasProvider> GetAllProviders() => Providers;

    // Identifies "the exact set of installed alias providers, at their exact versions" that
    // AliasGeneration baked into a snapshot's alias data. Deliberately independent of FilterFunc
    // (enabled/disabled state) -- installing or removing a provider, or a provider bumping its own
    // Version (an algorithm/rule change), is the only thing that can make previously-generated aliases
    // stale; toggling enabled/disabled never does (see GetAllProviders). Compared against a snapshot's
    // stored AliasProvidersFingerprint on load (mirrors IndexerHelper.ComputeExclusionFingerprint) --
    // a mismatch means a forced recompaction is needed to regenerate every unique name's aliases.
    public static string ComputeProvidersFingerprint()
    {
        var entries = GetAllProviders()
            .Select(p => $"{GetComponentId(p)}:{p.Version}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(e => e, StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var entry in entries)
            sb.Append(entry).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    /// <summary>
    /// A highly optimized helper to detect if a string contains non-ASCII characters.
    /// ASCII characters are in the range [0, 127].
    /// </summary>
    public static bool HasNonAscii(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] > 127)
                return true;
        }

        return false;
    }
}
