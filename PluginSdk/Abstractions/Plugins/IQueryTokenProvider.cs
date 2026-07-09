namespace SwiftList.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Represents a provider that can claim a token out of a search query's trailing
/// "&lt;keyword&gt; :a,b,c" suffix and transform the result list for it (e.g. sort, filter). Each
/// comma-separated token is dispatched to whichever registered provider's <see cref="CanHandle"/>
/// returns true first, and results are chained through providers in token order -- so multiple
/// providers can each own a different token and compose within one query.
/// </summary>
public interface IQueryTokenProvider
{
    /// <summary>
    /// A stable, locale-independent identifier for this provider.
    /// Used to persist the enabled/disabled state across language changes.
    /// Defaults to the concrete type name.
    /// </summary>
    string Id => GetType().Name;

    /// <summary>The name of the provider.</summary>
    string Name { get; }

    /// <summary>
    /// Returns true if this provider understands the given token (e.g. "s", ".txt.doc", or any
    /// custom token this plugin defines). Called once per token in the query's suffix; a token no
    /// provider claims is treated as an unsupported/typo'd filter -- the file/app results are dropped
    /// rather than silently applying only the tokens that were recognized (non-ordinary results, e.g.
    /// a calculator answer, are unrelated to a file filter and are kept regardless).
    /// </summary>
    bool CanHandle(string token);

    /// <summary>
    /// Transforms <paramref name="results"/> (already narrowed to ordinary file/app results -- no
    /// section headers or other synthetic rows) for the recognized <paramref name="token"/>. Return
    /// the transformed list (reordered and/or filtered, from the same result instances, never with
    /// items added). If the token(s) applied across the whole suffix don't actually shrink this set
    /// (e.g. a pure sort), the host re-merges the result back into the full result list, preserving
    /// the relative position of any non-ordinary rows; if the set does shrink, those non-ordinary
    /// rows (which describe the pre-filter result set) are dropped instead of re-merged.
    /// </summary>
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
}
