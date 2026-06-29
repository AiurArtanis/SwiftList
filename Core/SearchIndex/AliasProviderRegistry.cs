using System.Collections.Concurrent;
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

        var dllName = Path.GetFileName(provider.GetType().Assembly.Location);
        var typeName = provider.GetType().Name;
        var componentId = $"{dllName}::AliasProvider::{typeName}";

        var id = ProviderIdMap.GetOrAdd(componentId, _ => _nextId++);
        Logger.Log($"[AliasProviderRegistry] Registered alias provider: {provider.Name} with ID: {id} ({componentId})");
    }

    public static byte GetProviderId(IAliasProvider provider)
    {
        var dllName = Path.GetFileName(provider.GetType().Assembly.Location);
        var typeName = provider.GetType().Name;
        var componentId = $"{dllName}::AliasProvider::{typeName}";
        return ProviderIdMap.TryGetValue(componentId, out var id) ? id : (byte)0;
    }

    public static byte GetProviderIdByComponentId(string componentId) => ProviderIdMap.TryGetValue(componentId, out var id) ? id : (byte)255; // 255 represents not found

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

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] > 127)
                return true;
        }

        return false;
    }
}
