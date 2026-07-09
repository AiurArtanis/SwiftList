using SwiftList.PluginSdk.Abstractions;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Search;

// Dispatches the raw tokens split out of a query's trailing ":a,b,c" suffix to whichever registered
// IQueryTokenProvider plugin claims each one, chaining the result through providers in token order.
// Only "ordinary" (File/Application) results are exposed to providers -- once any token is active,
// section headers, "show N more", instant results (calculator, system settings, ...) and other
// synthetic rows describe or act on the untouched result set, not the token-narrowed one, so they're
// dropped rather than spliced back. A token no provider claims isn't silently ignored either -- it
// reads as a typo'd/unsupported filter, and silently showing the un-narrowed results would look like
// it worked when it didn't, so the whole query resolves to no results instead.
internal static class QueryTokenDispatcher
{
    public static async Task<List<AppSearchResult>> ApplyAsync(IReadOnlyList<AppSearchResult> results, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return results as List<AppSearchResult> ?? results.ToList();

        IReadOnlyList<ISearchResult> current = results.Where(IsOrdinaryResult).ToList();
        foreach (var token in tokens)
        {
            var provider = PluginManager.Instance.QueryTokenProviders.FirstOrDefault(p => p.CanHandle(token));
            if (provider == null)
                return new List<AppSearchResult>();

            current = await provider.ApplyAsync(token, current);
        }

        // Callers decide whether the resulting (possibly empty) list needs its own "no results"
        // placeholder (the quick/inline window renders one inline; the full search window already
        // has its own hint bound to an empty result list).
        return current.Cast<AppSearchResult>().ToList();
    }

    // Folders are ResultKind "File" too (IsDir just flags them) -- "Application" is the only other
    // kind that's a genuine file-backed result worth handing to a token provider.
    private static bool IsOrdinaryResult(AppSearchResult r) => r.ResultKind is "File" or "Application";
}
