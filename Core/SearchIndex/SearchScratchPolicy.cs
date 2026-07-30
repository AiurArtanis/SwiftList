namespace SwiftList.Core.SearchIndex;

/// <summary>
/// How large a reusable search buffer is allowed to get before it stops being worth keeping.
/// </summary>
/// <remarks>
/// The search path pools and reuses its working buffers because reallocating them on every keystroke was
/// measurable. What none of them did was ever give anything back: each grew to fit the biggest search it
/// had seen and stayed there, because Clear on a List or a Dictionary resets the count and not the
/// capacity. That was invisible while the full window asked for a thousand results; now that it asks for
/// every match on the drive, one search for a single letter sizes all of them for six hundred thousand
/// rows and the service holds that for the rest of its life.
///
/// It cannot be collected either -- these are reachable from static pools and thread statics, so it is
/// not garbage, which is why asking for a collection after a large search reclaimed the results
/// themselves and left this behind.
///
/// So a buffer that grew past this is released instead of retained. The threshold sits far above what
/// ordinary use produces, so a keystroke still finds its buffer already the right size and pays nothing;
/// only a search big enough to have caused the problem pays for one reallocation next time.
/// </remarks>
internal static class SearchScratchPolicy
{
    /// <summary>
    /// Entries above which a reused buffer is dropped rather than kept. Sixty-odd thousand is orders of
    /// magnitude more than a normal query matches and still only about a megabyte for the largest of
    /// these buffers, so retaining up to it costs little and reaching it is already unusual.
    /// </summary>
    public const int MaxRetainedEntries = 64 * 1024;

    /// <summary>Whether a buffer of this size is small enough to be worth holding on to.</summary>
    public static bool WorthRetaining(int entries) => entries <= MaxRetainedEntries;

    /// <summary>
    /// Empties a list, releasing its backing array outright when it had grown past the threshold.
    /// TrimExcess after Clear frees the array rather than merely forgetting the contents.
    /// </summary>
    public static void ClearAndTrim<T>(List<T> list)
    {
        list.Clear();
        if (!WorthRetaining(list.Capacity))
            list.TrimExcess();
    }

    /// <summary>Same, for a dictionary -- its buckets survive Clear exactly as a list's array does.</summary>
    public static void ClearAndTrim<TKey, TValue>(Dictionary<TKey, TValue> map) where TKey : notnull
    {
        var wasOversized = !WorthRetaining(map.Count);
        map.Clear();
        if (wasOversized)
            map.TrimExcess();
    }
}
