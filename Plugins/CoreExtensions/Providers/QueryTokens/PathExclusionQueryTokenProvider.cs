using System.IO;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Providers.QueryTokens;

// Built-in reference implementation of the "<keyword> ::<fzf-expr>" query suffix token (e.g.
// "<keyword> :xyz,:lgbb" alongside other tokens) -- keeps only results with a path segment (any
// ancestor folder name, or the file's own name) that matches the fzf expression, using the host's own
// fuzzy-match engine (FuzzyMatchService) so this behaves identically to a real search match, alias
// fallback included, instead of a hand-rolled substring check.
public class PathExclusionQueryTokenProvider : IQueryTokenProvider
{
    public string Name => TranslationService.Get("CoreExtensions_PathExclusionProvider_Name");

    public bool CanHandle(string token) => token.Length > 1 && token[0] == ':';

    public Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results)
    {
        var pattern = token[1..];
        if (string.IsNullOrWhiteSpace(pattern))
            return Task.FromResult(results);

        var filtered = results.Where(r => AnySegmentMatches(r.FullPath, pattern)).ToList();
        return Task.FromResult<IReadOnlyList<ISearchResult>>(filtered);
    }

    // The pattern this token actually fuzzy-matches against a path segment -- same text AnySegmentMatches
    // tests, so the host can highlight it too (e.g. "Rename" lighting up alongside the main keyword for
    // a "<keyword> ::rena" query, instead of only ever showing why the primary keyword matched).
    public string? GetHighlightText(string token) => token.Length > 1 ? token[1..] : null;

    // Splitting the full path covers every ancestor folder AND the file's own leaf name in one pass --
    // "keep if any path component matches" needs no special-casing between the two.
    private static bool AnySegmentMatches(string fullPath, string pattern)
    {
        foreach (var segment in fullPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (FuzzyMatchService.IsMatch(pattern, segment))
                return true;
        }
        return false;
    }
}
