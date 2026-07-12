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

    /// <summary>
    /// Maps each character position in a single alias string (one of the values <see cref="GetAliases"/>
    /// would yield for this exact <paramref name="text"/> -- not a '|'-joined group of alternatives,
    /// split those first) back to the index of the original character in <paramref name="text"/> it
    /// was derived from. Lets a match found against the alias (e.g. which pinyin letters matched) be
    /// translated onto the original text for highlighting, instead of highlighting nothing because the
    /// query never appears verbatim in the original (non-transliterated) text.
    /// Returns null if this alias wasn't produced by this provider for this text, or mapping isn't
    /// supported -- callers should treat that as "can't highlight via this provider", not an error.
    /// </summary>
    int[]? MapAliasToSourceIndices(string text, string alias) => null;
}
