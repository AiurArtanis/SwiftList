using SwiftList.PluginSdk.Abstractions;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Search;

// Dispatches the raw tokens split out of a query's trailing ":a,b,c" suffix to whichever registered
// IQueryTokenProvider plugin claims each one, chaining the result through providers in token order.
// Operates purely on the file/directory subset the caller hands it -- has no idea about (and doesn't
// try to reconstruct) section headers, instant results, applications, or anything else that ends up in
// the final UI list; composing the final result set around whatever this returns, and deciding what a
// zero-length result means for the UI, is entirely the caller's job.
internal static class QueryTokenDispatcher
{
    public static async Task<List<AppSearchResult>> ApplyAsync(IReadOnlyList<AppSearchResult> fileResults, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return fileResults as List<AppSearchResult> ?? fileResults.ToList();

        IReadOnlyList<ISearchResult> current = fileResults;
        foreach (var token in tokens)
        {
            var provider = PluginManager.Instance.QueryTokenProviders.FirstOrDefault(p => p.CanHandle(token));
            if (provider == null)
                // An unclaimed token reads as a typo'd/unsupported filter -- silently showing the
                // un-narrowed file/directory results would look like it worked when it didn't, so the
                // whole set is dropped. The caller decides what zero file/directory results means for
                // the rest of the UI (a "no results" placeholder, an unaffected instant-result row, ...).
                return new List<AppSearchResult>();

            current = await provider.ApplyAsync(token, current);
        }

        return current.Cast<AppSearchResult>().ToList();
    }
}
