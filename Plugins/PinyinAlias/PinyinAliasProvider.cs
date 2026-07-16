using System.Text;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.PinyinAlias;

public class PinyinAliasProvider : IAliasProvider, ITranslationProvider
{
    public string Name => TranslationService.Get("Plugins_PinyinAliasPluginName");

    public string Description => TranslationService.Get("Plugin_Comp_Desc_PinyinAliasProvider");


    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(System.Reflection.Assembly.GetExecutingAssembly());

    public IReadOnlyList<(char Start, char End)> InputRanges { get; } = new[] { PinyinEngine.TableRange };

    public IReadOnlyList<(char Start, char End)> OutputRanges { get; } = new[] { ('a', 'z') };

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LockObj = new();
    private static readonly string[][] AsciiSyllableCache;

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

    // Generation scratch, reused per thread: only the returned alias strings themselves are
    // allocated per call. _comboFullScratch is deliberately FIXED at 256 chars -- the combination
    // path's max-full-pinyin cap is part of the output contract (longer branches are pruned), and a
    // growable buffer here would make results depend on what a previous call happened to grow it to.
    [ThreadStatic] private static string[][]? _syllableScratch;
    [ThreadStatic] private static char[]? _fullBufferScratch;
    [ThreadStatic] private static char[]? _comboFullScratch;
    [ThreadStatic] private static char[]? _initialsScratch;
    [ThreadStatic] private static List<string>? _fullsListScratch;
    [ThreadStatic] private static List<string>? _initialsListScratch;
    [ThreadStatic] private static List<string>? _resultListScratch;
    [ThreadStatic] private static ushort[]?[]? _idScratch;
    [ThreadStatic] private static AliasByteSink? _fullCombosScratch;
    [ThreadStatic] private static AliasByteSink? _initialCombosScratch;
    [ThreadStatic] private static byte[]? _comboFullBytesScratch;
    [ThreadStatic] private static char[]? _comboInitialCharsScratch;

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

        var generated = GenerateAliases(text);

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

    private static string[] GenerateAliases(string text)
    {
        if (text.Length == 1)
        {
            // Single character fallback (needed for single-character queries)
            return PinyinEngine.TryGetPinyins(text[0], out var pinyins)
                ? pinyins
                : Array.Empty<string>();
        }

        var result = _resultListScratch ??= new List<string>(4);
        result.Clear();

        var lists = GetSyllableLists(text);

        var totalCombinations = 1;
        for (var i = 0; i < text.Length; i++)
        {
            totalCombinations *= lists[i].Length;
            if (totalCombinations > 32)
                break;
        }

        if (totalCombinations == 1)
        {
            var initialsArr = _initialsScratch;
            if (initialsArr == null || initialsArr.Length < text.Length)
                _initialsScratch = initialsArr = new char[Math.Max(text.Length, 64)];

            var fullLen = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var s = lists[i][0];
                initialsArr[i] = s.Length > 0 ? s[0] : '\0';
                fullLen += s.Length;
            }

            var initialAlias = new string(initialsArr, 0, text.Length);
            result.Add(initialAlias);

            var fullBuffer = _fullBufferScratch;
            if (fullBuffer == null || fullBuffer.Length < fullLen)
                _fullBufferScratch = fullBuffer = new char[Math.Max(fullLen, 256)];

            var offset = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var s = lists[i][0];
                s.CopyTo(0, fullBuffer, offset, s.Length);
                offset += s.Length;
            }
            var fullAlias = new string(fullBuffer, 0, fullLen);
            if (fullAlias != initialAlias)
                result.Add(fullAlias);
            return result.ToArray();
        }

        var fullPinyins = _fullsListScratch ??= new List<string>(32);
        var initials = _initialsListScratch ??= new List<string>(32);
        fullPinyins.Clear();
        initials.Clear();
        var count = 0;
        var steps = 0;

        var fullBufferTemp = _comboFullScratch ??= new char[256];
        var initialsBuffer = _initialsScratch;
        if (initialsBuffer == null || initialsBuffer.Length < text.Length)
            _initialsScratch = initialsBuffer = new char[Math.Max(text.Length, 64)];

        // Generate combinations. Since we concatenate them, we can safely allow up to 32 combinations
        // to support longer polyphonic names without database explosion.
        GenerateCombinations(lists, text.Length, 0, 0, fullPinyins, initials, fullBufferTemp, initialsBuffer, ref count, ref steps);

        var joinedInitials = JoinUnique(initials);
        if (joinedInitials != null)
            result.Add(joinedInitials);

        var joinedFulls = JoinUnique(fullPinyins);
        if (joinedFulls != null && !joinedFulls.Equals(joinedInitials, StringComparison.OrdinalIgnoreCase))
            result.Add(joinedFulls);

        return result.ToArray();
    }

    // Dedup preserving insertion order (List.Contains semantics), '|'-joined; n <= 32 so a linear
    // scan beats a HashSet allocation at this size.
    private static string? JoinUnique(List<string> values)
    {
        string? single = null;
        List<string>? unique = null;
        foreach (var v in values)
        {
            if (string.IsNullOrWhiteSpace(v))
                continue;
            if (single == null)
            {
                single = v;
                continue;
            }
            if (unique == null)
            {
                if (v == single)
                    continue;
                unique = new List<string>(4) { single, v };
                continue;
            }
            if (!unique.Contains(v))
                unique.Add(v);
        }

        if (unique != null)
            return string.Join('|', unique);
        return single;
    }

    // "alias" here is one single combination already (caller splits '|'-joined alternatives first).
    public int[]? MapAliasToSourceIndices(string text, string alias)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(alias))
            return null;

        var lists = GetSyllableLists(text);

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

    private static string[][] GetSyllableLists(string text)
    {
        var lists = _syllableScratch;
        if (lists == null || lists.Length < text.Length)
            _syllableScratch = lists = new string[Math.Max(text.Length, 64)][];

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (PinyinEngine.TryGetPinyins(c, out var pinyins))
            {
                lists[i] = pinyins;
            }
            else if (c < 128)
            {
                lists[i] = AsciiSyllableCache[c];
            }
            else
            {
                lists[i] = new string[] { char.ToLowerInvariant(c).ToString() };
            }
        }
        return lists;
    }

    private static void GenerateCombinations(
        string[][] lists,
        int listCount,
        int index,
        int currentFullLength,
        List<string> fullPinyins,
        List<string> initials,
        char[] fullBuffer,
        char[] initialsBuffer,
        ref int count,
        ref int steps)
    {
        // Steps budget: the 32-combination cap below only counts FULL-depth completions, but the
        // fullBuffer-overflow check prunes branches BEFORE full depth -- a long name (full pinyin
        // longer than the buffer) dense with polyphonic characters means no branch ever completes,
        // the cap never fires, and the recursion explores the whole combinatorial tree (a 240-char
        // all-polyphonic name explored ~2^55 paths and hung the process). The budget covers every
        // legitimate enumeration and turns the pathological case into an immediate bounded bail-out.
        if (++steps > listCount * 32 + 256) return;
        if (count >= 32) return; // Limit to 32 combinations to prevent combinatorial explosion

        if (index == listCount)
        {
            fullPinyins.Add(new string(fullBuffer, 0, currentFullLength));
            initials.Add(new string(initialsBuffer, 0, listCount));
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
                GenerateCombinations(lists, listCount, index + 1, currentFullLength + element.Length, fullPinyins, initials, fullBuffer, initialsBuffer, ref count, ref steps);
            }
        }
    }

    // ─── Byte-native path (GetAliasesUtf8 override) ────────────────────────────────────────────
    // Used by the host's bulk indexing path: assembles aliases directly from pre-encoded syllable
    // bytes into the sink, never materializing a string. Verified byte-identical (decoded) to the
    // string path across 200k-name equivalence runs plus adversarial corpora before adoption.

    public void GetAliasesUtf8(string text, AliasByteSink dest)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (text.Length == 1)
        {
            if (PinyinEngine.TryGetPinyinIds(text[0], out var soloIds))
            {
                foreach (var id in soloIds)
                {
                    var start = dest.BeginSegment();
                    dest.Append(PinyinEngine.GetSyllableUtf8(id));
                    dest.EndSegment(start);
                }
            }
            return;
        }

        var ids = _idScratch;
        if (ids == null || ids.Length < text.Length)
            _idScratch = ids = new ushort[]?[Math.Max(text.Length, 64)];

        // Fill EVERY position first, THEN count: breaking out of a fused fill+count loop early
        // would leave stale entries from a previous call in the thread-static scratch beyond the
        // break point.
        for (var i = 0; i < text.Length; i++)
            ids[i] = PinyinEngine.TryGetPinyinIds(text[i], out var charIds) ? charIds : null;

        var totalCombinations = 1L;
        for (var i = 0; i < text.Length; i++)
        {
            totalCombinations *= ids[i]?.Length ?? 1;
            if (totalCombinations > 32)
                break;
        }

        if (totalCombinations == 1)
        {
            var initialsStart = dest.BeginSegment();
            for (var i = 0; i < text.Length; i++)
                AppendInitial(dest, text, i, ids[i]);
            dest.EndSegment(initialsStart);

            var fullStart = dest.BeginSegment();
            for (var i = 0; i < text.Length; i++)
                AppendFull(dest, text, i, ids[i]);

            // Same "full == initials -> yield once" rule as the string path.
            if (dest.Pending(fullStart).SequenceEqual(dest.Segment(dest.SegmentCount - 1)))
                dest.AbandonSegment(fullStart);
            else
                dest.EndSegment(fullStart);
            return;
        }

        // Combination (polyphonic) path -- byte-native mirror of GenerateCombinations, same steps
        // budget, same fixed 256-byte full-pinyin cap.
        var fulls = _fullCombosScratch ??= new AliasByteSink();
        var initials = _initialCombosScratch ??= new AliasByteSink();
        fulls.Reset();
        initials.Reset();

        var fullBuffer = _comboFullBytesScratch ??= new byte[256];
        var initialBuffer = _comboInitialCharsScratch;
        if (initialBuffer == null || initialBuffer.Length < text.Length)
            _comboInitialCharsScratch = initialBuffer = new char[Math.Max(text.Length, 64)];

        var count = 0;
        var steps = 0;
        RecurseBytes(text, ids, 0, 0, fulls, initials, fullBuffer, initialBuffer, ref count, ref steps);

        var initialsGroupStart = dest.BeginSegment();
        JoinUniqueSegments(initials, dest);
        var hadInitials = dest.Pending(initialsGroupStart).Length > 0;
        dest.EndSegment(initialsGroupStart);

        var fullsGroupStart = dest.BeginSegment();
        JoinUniqueSegments(fulls, dest);
        if (dest.Pending(fullsGroupStart).Length == 0)
        {
            dest.AbandonSegment(fullsGroupStart);
        }
        else if (hadInitials && dest.Pending(fullsGroupStart).SequenceEqual(dest.Segment(dest.SegmentCount - 1)))
        {
            dest.AbandonSegment(fullsGroupStart);
        }
        else
        {
            dest.EndSegment(fullsGroupStart);
        }
    }

    private static void AppendInitial(AliasByteSink dest, string text, int i, ushort[]? charIds)
    {
        if (charIds != null)
            dest.Append(PinyinEngine.GetSyllableUtf8(charIds[0])[0]);
        else
            AppendLiteralChar(dest, text[i], i > 0 ? text[i - 1] : '\0', i + 1 < text.Length ? text[i + 1] : '\0');
    }

    private static void AppendFull(AliasByteSink dest, string text, int i, ushort[]? charIds)
    {
        if (charIds != null)
            dest.Append(PinyinEngine.GetSyllableUtf8(charIds[0]));
        else
            AppendLiteralChar(dest, text[i], i > 0 ? text[i - 1] : '\0', i + 1 < text.Length ? text[i + 1] : '\0');
    }

    // Encodes one literal (non-CJK) source position. Every position emits exactly one alias element
    // in order, so a surrogate pair's halves always land adjacent -- the string path re-pairs them
    // inside the alias string for free, and here the pair must be encoded together (a UTF-16 half
    // encoded alone is invalid UTF-8 and turns into U+FFFD, corrupting emoji/CJK-extension chars).
    // The HIGH half emits the whole pair's bytes; the matching LOW half then emits nothing.
    private static void AppendLiteralChar(AliasByteSink dest, char c, char prev, char next)
    {
        if (char.IsHighSurrogate(c) && char.IsLowSurrogate(next))
        {
            Span<byte> tmp = stackalloc byte[4];
            var written = new Rune(c, next).EncodeToUtf8(tmp);
            dest.Append(tmp[..written]);
            return;
        }
        if (char.IsLowSurrogate(c) && char.IsHighSurrogate(prev))
            return;

        var lower = char.ToLowerInvariant(c);
        if (lower < 128)
            dest.Append((byte)lower);
        else
            AppendUtf8Char(dest, lower);
    }

    private static void AppendUtf8Char(AliasByteSink dest, char c)
    {
        Span<byte> tmp = stackalloc byte[4];
        Span<char> one = stackalloc char[1];
        one[0] = c;
        var written = Encoding.UTF8.GetBytes(one, tmp);
        dest.Append(tmp[..written]);
    }

    private static void RecurseBytes(
        string text,
        ushort[]?[] ids,
        int index,
        int fullLen,
        AliasByteSink fulls,
        AliasByteSink initials,
        byte[] fullBuffer,
        char[] initialBuffer,
        ref int count,
        ref int steps)
    {
        // Same steps budget as GenerateCombinations -- see the comment there.
        if (++steps > text.Length * 32 + 256) return;
        if (count >= 32) return;

        if (index == text.Length)
        {
            var fs = fulls.BeginSegment();
            fulls.Append(fullBuffer.AsSpan(0, fullLen));
            fulls.EndSegment(fs);

            var istart = initials.BeginSegment();
            for (var i = 0; i < text.Length; i++)
            {
                var c = initialBuffer[i];
                if (c < 128) initials.Append((byte)c);
                else AppendLiteralChar(initials, c, i > 0 ? initialBuffer[i - 1] : '\0', i + 1 < text.Length ? initialBuffer[i + 1] : '\0');
            }
            initials.EndSegment(istart);
            count++;
            return;
        }

        var charIds = ids[index];
        if (charIds == null)
        {
            var c = text[index];
            int written;
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                Span<byte> tmp = stackalloc byte[4];
                written = new Rune(c, text[index + 1]).EncodeToUtf8(tmp);
                if (fullLen + written > fullBuffer.Length) return;
                tmp[..written].CopyTo(fullBuffer.AsSpan(fullLen));
                initialBuffer[index] = c;
            }
            else if (char.IsLowSurrogate(c) && index > 0 && char.IsHighSurrogate(text[index - 1]))
            {
                written = 0;
                initialBuffer[index] = c;
            }
            else
            {
                var lower = char.ToLowerInvariant(c);
                if (lower < 128)
                {
                    if (fullLen + 1 > fullBuffer.Length) return;
                    fullBuffer[fullLen] = (byte)lower;
                    written = 1;
                }
                else
                {
                    Span<byte> tmp = stackalloc byte[4];
                    Span<char> one = stackalloc char[1];
                    one[0] = lower;
                    written = Encoding.UTF8.GetBytes(one, tmp);
                    if (fullLen + written > fullBuffer.Length) return;
                    tmp[..written].CopyTo(fullBuffer.AsSpan(fullLen));
                }
                initialBuffer[index] = lower;
            }
            RecurseBytes(text, ids, index + 1, fullLen + written, fulls, initials, fullBuffer, initialBuffer, ref count, ref steps);
            return;
        }

        foreach (var id in charIds)
        {
            var syl = PinyinEngine.GetSyllableUtf8(id);
            if (fullLen + syl.Length > fullBuffer.Length)
                continue;
            syl.CopyTo(fullBuffer.AsSpan(fullLen));
            initialBuffer[index] = (char)syl[0];
            RecurseBytes(text, ids, index + 1, fullLen + syl.Length, fulls, initials, fullBuffer, initialBuffer, ref count, ref steps);
        }
    }

    // Appends the unique segments of `source` (first-seen order, matching the string path's
    // List.Contains dedup) joined by '|' into the currently-open segment of `dest`.
    private static void JoinUniqueSegments(AliasByteSink source, AliasByteSink dest)
    {
        var wroteAny = false;
        for (var i = 0; i < source.SegmentCount; i++)
        {
            var seg = source.Segment(i);
            if (seg.IsEmpty)
                continue;

            var duplicate = false;
            for (var j = 0; j < i; j++)
            {
                if (source.Segment(j).SequenceEqual(seg))
                {
                    duplicate = true;
                    break;
                }
            }
            if (duplicate)
                continue;

            if (wroteAny)
                dest.Append((byte)'|');
            dest.Append(seg);
            wroteAny = true;
        }
    }
}
