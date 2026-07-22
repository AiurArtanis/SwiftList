using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.PinyinAlias;

public class PinyinAliasProvider : IAliasProvider, ITranslationProvider
{
    public string Name => TranslationService.Get("Plugins_PinyinAliasPluginName");

    public string Description => TranslationService.Get("Plugin_Comp_Desc_PinyinAliasProvider");

    public int Version => 2;

    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(System.Reflection.Assembly.GetExecutingAssembly());

    public IReadOnlyList<(char Start, char End)> InputRanges { get; } = new[] { PinyinEngine.TableRange };

    public IReadOnlyList<(char Start, char End)> OutputRanges { get; } = new[] { ('a', 'z') };

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LockObj = new();

    // Live-path result cache: FuzzyMatcher.IsMatch, HighlightMask, and PathGate's live fallback all
    // regenerate aliases for the SAME texts on every keystroke (e.g. an instant-result plugin
    // fuzzy-scanning thousands of titles per keypress). Two bounded generations, swapped when the
    // current one fills, keep memory capped with LRU-ish retention -- measured ~20x on that path.
    // Values are immutable arrays, safe to hand to any number of callers. The bulk indexing path
    // uses GetAliasesUtf8 instead and never touches this cache.
    private const int ResultCacheCap = 4096;
    private static readonly object ResultCacheLock = new();
    private static Dictionary<string, string[]> _resultCacheCur = new(StringComparer.Ordinal);
    private static Dictionary<string, string[]> _resultCachePrev = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName)
    {
        lock (LockObj)
        {
            if (Cache.TryGetValue(cultureName, out var cached))
            {
                return cached;
            }

            var translations = TranslationService.LoadEmbeddedTranslations(System.Reflection.Assembly.GetExecutingAssembly(), cultureName, "Plugin");
            Cache[cultureName] = translations;
            return translations;
        }
    }

    public bool CanHandle(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // Vectorized range pre-gate rejects text with no char in the table's range at SIMD speed;
        // only in-range candidates pay for precise per-char table lookups.
        if (!PinyinEngine.MayContainChinese(text))
            return false;

        for (var i = 0; i < text.Length; i++)
        {
            if (PinyinEngine.IsChinese(text[i]))
                return true;
        }

        return false;
    }

    public IEnumerable<string> GetAliases(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();

        lock (ResultCacheLock)
        {
            if (_resultCacheCur.TryGetValue(text, out var cached))
                return cached;
            if (_resultCachePrev.TryGetValue(text, out cached))
            {
                _resultCacheCur[text] = cached; // promote so it survives the next swap
                return cached;
            }
        }

        var generated = PinyinAliasCombinationGenerator.GenerateAliases(text);

        lock (ResultCacheLock)
        {
            if (_resultCacheCur.Count >= ResultCacheCap)
            {
                (_resultCachePrev, _resultCacheCur) = (_resultCacheCur, _resultCachePrev);
                _resultCacheCur.Clear();
            }
            _resultCacheCur[text] = generated;
        }

        return generated;
    }

    // "alias" here is one single combination already (caller splits '|'-joined alternatives first).
    public int[]? MapAliasToSourceIndices(string text, string alias)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(alias))
            return null;

        var lists = PinyinAliasCombinationGenerator.GetSyllableLists(text);

        // Fast path: the "initials" alias contributes exactly one character per source character.
        // Verify it actually looks like initials (each alias char is the first letter of one of that
        // source character's own candidate syllables) rather than assuming from length alone --
        // coincidentally-equal lengths do happen (e.g. every character single-letter-syllable), and a
        // wrong identity mapping would silently mis-highlight rather than fail loudly.
        if (alias.Length == text.Length)
        {
            var isInitials = true;
            for (var i = 0; i < text.Length; i++)
            {
                var initial = char.ToLowerInvariant(alias[i]);
                var candidateMatches = false;
                foreach (var candidate in lists[i])
                {
                    if (candidate.Length > 0 && char.ToLowerInvariant(candidate[0]) == initial)
                    {
                        candidateMatches = true;
                        break;
                    }
                }
                if (!candidateMatches)
                {
                    isInitials = false;
                    break;
                }
            }

            if (isInitials)
            {
                var identity = new int[text.Length];
                for (var i = 0; i < text.Length; i++)
                    identity[i] = i;
                return identity;
            }
        }

        // General path: the "full pinyin" alias concatenates each character's whole syllable, which
        // can be more than one letter -- greedily walk source characters, consuming whichever
        // candidate syllable the alias actually continues with at the current position. This can
        // only mis-segment on genuinely ambiguous polyphonic overlaps; bailing out to null (no
        // highlight via this provider) is safe and no worse than today's total lack of one.
        var map = new int[alias.Length];
        var aliasPos = 0;
        for (var sourceIndex = 0; sourceIndex < text.Length && aliasPos < alias.Length; sourceIndex++)
        {
            var matchedLen = -1;
            foreach (var candidate in lists[sourceIndex])
            {
                if (candidate.Length > 0 && aliasPos + candidate.Length <= alias.Length &&
                    string.Compare(alias, aliasPos, candidate, 0, candidate.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    matchedLen = candidate.Length;
                    break;
                }
            }

            if (matchedLen < 0)
                return null;

            for (var j = 0; j < matchedLen; j++)
                map[aliasPos + j] = sourceIndex;
            aliasPos += matchedLen;
        }

        return aliasPos == alias.Length ? map : null;
    }

    // Byte-native mirror of GetAliases, used by the host's bulk indexing path; extracted to
    // PinyinAliasUtf8Encoder (composition, not a partial class) purely to keep this file under the
    // repo's per-file line limit.
    public void GetAliasesUtf8(string text, AliasByteSink dest) => PinyinAliasUtf8Encoder.Encode(text, dest);
}
