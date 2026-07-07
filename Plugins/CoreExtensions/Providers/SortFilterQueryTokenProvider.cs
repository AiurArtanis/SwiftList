using System.IO;
using System.Text.RegularExpressions;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Providers;

// Built-in reference implementation of the "<keyword> :[SCMA]" (sort by Size/Created/Modified/
// Accessed) and ".ext.ext2" (extension filter) query suffix tokens.
public class SortFilterQueryTokenProvider : IQueryTokenProvider
{
    private static readonly Regex SortTokenPattern = new(@"^-?[SCMA]-?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Name => TranslationService.Get("CoreExtensions_QueryTokenProvider_Name");

    public bool CanHandle(string token) => IsFilterToken(token) || SortTokenPattern.IsMatch(token);

    public async Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results)
    {
        if (IsFilterToken(token))
            return ApplyFilter(token, results);

        return await ApplySortAsync(token, results);
    }

    private static bool IsFilterToken(string token) => token.Length > 1 && token[0] == '.';

    private static IReadOnlyList<ISearchResult> ApplyFilter(string token, IReadOnlyList<ISearchResult> results)
    {
        var extensions = token.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.ToLowerInvariant())
            .ToHashSet();
        if (extensions.Count == 0)
            return results;

        return results.Where(r => !r.IsDir && extensions.Contains(Path.GetExtension(r.FullPath).TrimStart('.').ToLowerInvariant())).ToList();
    }

    private static async Task<IReadOnlyList<ISearchResult>> ApplySortAsync(string token, IReadOnlyList<ISearchResult> results)
    {
        var descending = token[0] == '-' || token[^1] == '-';
        var letter = char.ToUpperInvariant(token.Trim('-')[0]);

        // ISearchResult.DateModified is itself lazily/asynchronously loaded (same as Size/Created/
        // Accessed would be if read directly) -- reading it synchronously here would silently sort
        // against unloaded placeholders. Route every field through the awaited batch lookup instead.
        var paths = results.Select(r => r.FullPath).Distinct().ToList();
        var metadata = await FileMetadataService.GetMetadataAsync(paths);

        Func<ISearchResult, IComparable> keySelector = letter switch
        {
            'S' => r => metadata.TryGetValue(r.FullPath, out var m) ? m.Size : 0,
            'C' => r => metadata.TryGetValue(r.FullPath, out var m) ? m.Created : DateTime.MinValue,
            'M' => r => metadata.TryGetValue(r.FullPath, out var m) ? m.Modified : DateTime.MinValue,
            'A' => r => metadata.TryGetValue(r.FullPath, out var m) ? m.Accessed : DateTime.MinValue,
            _ => r => r.Name
        };

        var ordered = descending ? results.OrderByDescending(keySelector) : results.OrderBy(keySelector);
        return ordered.ToList();
    }
}
