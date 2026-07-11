namespace SwiftList.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Defines a provider that generates search aliases/transliterations for non-ASCII text.
/// </summary>
public interface IAliasProvider
{
    /// <summary>
    /// A display name for the provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Determines if this provider can handle/transliterate the given text.
    /// </summary>
    bool CanHandle(string text);

    /// <summary>
    /// Generates aliases for the given text.
    /// </summary>
    /// <param name="text">The original text.</param>
    /// <returns>A collection of generated aliases.</returns>
    IEnumerable<string> GetAliases(string text);

    /// <summary>
    /// Bump this when this provider's alias output could change for the same input (algorithm fix,
    /// new rule, updated data table). The index uses it to detect that previously-generated aliases
    /// from this provider are stale and need regenerating.
    /// </summary>
    int Version => 1;
}
