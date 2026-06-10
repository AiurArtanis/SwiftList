namespace SwiftList.PluginSdk;

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
}
