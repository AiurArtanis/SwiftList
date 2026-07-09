using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core;

// A standalone, public entry point for the exact "name, falling back to alias" matching rule the core
// index scan already applies per record (see RecordSearch/CacheExtensions.cs and its siblings), for
// callers that need identical matching semantics without running an actual index scan -- e.g. a query
// token provider filtering already-fetched results by fzf pattern against something other than a
// record's own name (a path segment, in PathExclusionQueryTokenProvider's case). FzfPattern itself stays
// internal; this is the one seam meant to cross the assembly boundary (see PluginSdk.Services.
// FuzzyMatchService, wired to this in PluginManager).
public static class FuzzyMatcher
{
    public static bool IsMatch(string pattern, string text)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(text))
            return false;

        var fzf = FzfPattern.Parse(pattern);
        if (fzf.TryMatch(text, out _, FzfScoringScheme.Default))
            return true;

        if (!AliasProviderRegistry.HasNonAscii(text))
            return false;

        var disabledIds = SearchContext.DisabledAliasIds;
        var queryLen = fzf.GetTotalTermLength();

        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            if (disabledIds != null && disabledIds.Contains(AliasProviderRegistry.GetProviderId(provider)))
                continue;

            if (!provider.CanHandle(text))
                continue;

            foreach (var alias in provider.GetAliases(text))
            {
                if (!fzf.TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default))
                    continue;

                // Same quality bar the core index scan applies to its own alias fallback (see
                // RecordSearch/CacheExtensions.cs) -- reject a match whose span is disproportionately
                // wider than the query, or whose score is too low, so a weak coincidental alias hit
                // doesn't count as a match here either.
                var span = aliasMatch.MaxEnd - aliasMatch.MinBegin;
                if (span > Math.Max(queryLen * 3, 20) || aliasMatch.Score < queryLen * 5)
                    continue;

                return true;
            }
        }

        return false;
    }
}
