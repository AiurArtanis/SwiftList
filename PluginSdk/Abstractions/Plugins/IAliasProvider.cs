namespace SwiftList.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Defines a provider that generates search aliases/transliterations for non-ASCII text.
/// </summary>
public interface IAliasProvider : IPluginComponent
{

    /// <summary>
    /// Determines if this provider can handle/transliterate the given text.
    /// </summary>
    bool CanHandle(string text);

    /// <summary>
    /// The character range(s) this provider transliterates FROM -- the alphabet of the literal
    /// source text it recognizes (e.g. the CJK ideograph block, for pinyin). Used to split a query
    /// term that mixes this alphabet with <see cref="OutputRanges"/> (e.g. "大cj") into a literal
    /// run to match against the candidate's own text, and an alias-syntax run to match against this
    /// provider's generated alias -- see <c>Core.SearchIndex.MixedQueryMatcher</c>.
    /// </summary>
    IReadOnlyList<(char Start, char End)> InputRanges { get; }

    /// <summary>
    /// The character range(s) this provider's generated aliases are made of (e.g. lowercase a-z,
    /// for pinyin). Paired with <see cref="InputRanges"/> for mixed-query segmentation.
    /// </summary>
    IReadOnlyList<(char Start, char End)> OutputRanges { get; }

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

    /// <summary>
    /// Byte-native variant of <see cref="GetAliases"/> used on the host's bulk indexing path, where
    /// aliases are ultimately stored as UTF-8 bytes: emits each alias as a UTF-8 segment into
    /// <paramref name="dest"/>. Segments must be lowercase and must decode to exactly the strings
    /// <see cref="GetAliases"/> would yield (minus empty/whitespace-only entries).
    /// The default implementation adapts the string API, so existing providers work unchanged;
    /// override it to skip string materialization entirely when generation volume matters.
    /// </summary>
    void GetAliasesUtf8(string text, AliasByteSink dest)
    {
        foreach (var alias in GetAliases(text))
        {
            if (string.IsNullOrWhiteSpace(alias))
                continue;
            dest.AddString(NeedsLower(alias) ? alias.ToLowerInvariant() : alias);
        }
    }

    private static bool NeedsLower(string s)
    {
        foreach (var c in s)
        {
            if (char.ToLowerInvariant(c) != c)
                return true;
        }
        return false;
    }
}
