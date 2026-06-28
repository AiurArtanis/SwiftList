using SwiftList.PluginSdk;

namespace SwiftList.Plugins.PinyinAlias;

public class PinyinAliasProvider : IAliasProvider, ITranslationProvider
{
    public string Name => TranslationService.Get("Plugins_PinyinAliasPluginName");

    string ITranslationProvider.Name => "Pinyin Translation Provider";

    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(System.Reflection.Assembly.GetExecutingAssembly());

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LockObj = new();
    private static readonly string[][] AsciiSyllableCache;

    static PinyinAliasProvider()
    {
        AsciiSyllableCache = new string[128][];
        for (var i = 0; i < 128; i++)
        {
            AsciiSyllableCache[i] = new string[] { ((char)i).ToString().ToLowerInvariant() };
        }
    }

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
            yield break;

        if (text.Length == 1)
        {
            // Single character fallback (needed for FuzzyHighlightMatcher and single-character queries)
            if (PinyinEngine.TryGetPinyins(text[0], out var pinyins))
            {
                foreach (var p in pinyins)
                {
                    yield return p;
                }
            }
            yield break;
        }

        var lists = GetSyllableLists(text);
        
        // Fast path: check if there's only 1 combination (no polyphonic characters)
        var totalCombinations = 1;
        for (var i = 0; i < lists.Length; i++)
        {
            totalCombinations *= lists[i].Length;
            if (totalCombinations > 32)
                break;
        }

        if (totalCombinations == 1)
        {
            var initialsArr = new char[lists.Length];
            var fullLen = 0;
            for (var i = 0; i < lists.Length; i++)
            {
                var s = lists[i][0];
                initialsArr[i] = s.Length > 0 ? s[0] : '\0';
                fullLen += s.Length;
            }

            var initialAlias = new string(initialsArr);
            yield return initialAlias;

            var fullBuffer = new char[fullLen];
            var offset = 0;
            for (var i = 0; i < lists.Length; i++)
            {
                var s = lists[i][0];
                s.CopyTo(0, fullBuffer, offset, s.Length);
                offset += s.Length;
            }
            var fullAlias = new string(fullBuffer);
            if (fullAlias != initialAlias)
            {
                yield return fullAlias;
            }
            yield break;
        }

        var fullPinyins = new List<string>();
        var initials = new List<string>();
        var count = 0;

        var fullBufferTemp = new char[256];
        var initialsBuffer = new char[lists.Length];

        // Generate combinations. Since we concatenate them, we can safely allow up to 32 combinations
        // to support longer polyphonic names without database explosion.
        GenerateCombinations(lists, 0, 0, fullPinyins, initials, fullBufferTemp, initialsBuffer, ref count);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Yield the concatenated initials string (e.g. "cqs|zqs")
        var uniqueInitials = new List<string>();
        foreach (var init in initials)
        {
            if (!string.IsNullOrWhiteSpace(init))
            {
                if (!uniqueInitials.Contains(init))
                    uniqueInitials.Add(init);
            }
        }
        if (uniqueInitials.Count > 0)
        {
            var joinedInitials = string.Join("|", uniqueInitials);
            if (seen.Add(joinedInitials))
            {
                yield return joinedInitials;
            }
        }

        // 2. Yield the concatenated full pinyin string (e.g. "chongqingshi|zhongqingshi")
        var uniqueFulls = new List<string>();
        foreach (var fp in fullPinyins)
        {
            if (!string.IsNullOrWhiteSpace(fp))
            {
                if (!uniqueFulls.Contains(fp))
                    uniqueFulls.Add(fp);
            }
        }
        if (uniqueFulls.Count > 0)
        {
            var joinedFulls = string.Join("|", uniqueFulls);
            if (seen.Add(joinedFulls))
            {
                yield return joinedFulls;
            }
        }
    }

    private static string[][] GetSyllableLists(string text)
    {
        var lists = new string[text.Length][];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (PinyinEngine.TryGetPinyins(c, out var pinyins))
            {
                lists[i] = pinyins;
            }
            else
            {
                if (c < 128)
                {
                    lists[i] = AsciiSyllableCache[c];
                }
                else
                {
                    lists[i] = new string[] { char.ToLowerInvariant(c).ToString() };
                }
            }
        }
        return lists;
    }

    private static void GenerateCombinations(
        string[][] lists,
        int index,
        int currentFullLength,
        List<string> fullPinyins,
        List<string> initials,
        char[] fullBuffer,
        char[] initialsBuffer,
        ref int count)
    {
        if (count >= 32) return; // Limit to 32 combinations to prevent combinatorial explosion

        if (index == lists.Length)
        {
            fullPinyins.Add(new string(fullBuffer, 0, currentFullLength));
            initials.Add(new string(initialsBuffer, 0, lists.Length));
            count++;
            return;
        }

        var elements = lists[index];
        foreach (var element in elements)
        {
            if (currentFullLength + element.Length <= fullBuffer.Length)
            {
                element.CopyTo(0, fullBuffer, currentFullLength, element.Length);
                initialsBuffer[index] = element.Length > 0 ? element[0] : '\0';
                GenerateCombinations(lists, index + 1, currentFullLength + element.Length, fullPinyins, initials, fullBuffer, initialsBuffer, ref count);
            }
        }
    }
}
