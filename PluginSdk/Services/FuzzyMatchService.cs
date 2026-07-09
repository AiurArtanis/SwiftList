namespace SwiftList.PluginSdk.Services;

/// <summary>
/// A decoupled service exposing the host's own fuzzy-match engine (the same matching used for the
/// primary search, including its alias/transliteration fallback) to plugins that need identical
/// matching semantics without reimplementing a fuzzy matcher of their own.
/// </summary>
public static class FuzzyMatchService
{
    /// <summary>
    /// Delegate set by the host application. Parameters: (pattern, text) -- returns whether
    /// <paramref name="text"/>, or one of its generated aliases, matches the fzf-syntax
    /// <paramref name="pattern"/>.
    /// </summary>
    public static Func<string, string, bool>? IsMatchFunc { get; set; }

    /// <summary>
    /// Returns whether <paramref name="text"/> matches the fzf-syntax <paramref name="pattern"/>,
    /// using the exact same matching (and alias fallback) rule the host's own search uses.
    /// </summary>
    public static bool IsMatch(string pattern, string text) => IsMatchFunc?.Invoke(pattern, text) ?? false;
}
