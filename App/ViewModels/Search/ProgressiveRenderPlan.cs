namespace SwiftList.App.ViewModels.Search;

/// <summary>
/// Decides, tick by tick, how much of a still-arriving result stream the next intermediate render
/// should take in.
/// </summary>
/// <remarks>
/// What is capped here is the SIZE OF EACH BITE, not a ceiling on the total. An earlier version capped
/// the total, on the theory that rows past a hundred thousand exist for the scrollbar rather than to be
/// read -- but a list that climbs and then stops dead at a round number for the rest of a multi-second
/// search reads as a hang, which is the exact impression progressive rendering exists to remove. The
/// count has to keep moving until it is done.
///
/// It can keep moving because <see cref="Mapping.StreamingResultAccumulator"/> maps each result exactly
/// once, so a bite is paid for once and never re-done. The bite size still ramps -- the first paint
/// should land immediately, not after the backlog that piled up before the first tick -- but it ramps
/// to a steady maximum and then holds there, delivering that many more rows every tick until the stream
/// is drained.
/// </remarks>
internal sealed class ProgressiveRenderPlan
{
    // Small enough that the first paint is essentially free, and still several screens' worth of rows.
    internal const int InitialBite = 2_000;

    // Roughly a quarter-second of mapping and ranking. Larger bites finish the whole set in fewer
    // ticks, but each tick is one visible step, and steps that take much longer than this stop reading
    // as a list filling up and start reading as a list stalling.
    internal const int MaxBite = 100_000;

    internal const int BiteGrowthFactor = 4;

    // Below this the list is too short to be worth an intermediate paint at all -- the search is about
    // to finish and render it in full. Matches the threshold the one-shot render used.
    internal const int MinimumFirstRender = 9;

    private int _bite = InitialBite;
    private int _rendered;

    /// <summary>Total rows covered by the most recent accepted render.</summary>
    public int Rendered => _rendered;

    /// <summary>Most rows the next accepted render will add.</summary>
    public int Bite => _bite;

    /// <summary>
    /// Given the number of results received so far, returns the TOTAL to render now (never more than
    /// one bite beyond the previous render), or 0 to skip this tick. A non-zero return advances the
    /// plan, so each call represents one render actually happening.
    /// </summary>
    public int NextRenderSize(int received)
    {
        if (_rendered == 0 && received < MinimumFirstRender)
            return 0;

        var take = Math.Min(received, (long)_rendered + _bite);
        if (take <= _rendered)
            return 0;

        _rendered = (int)take;
        _bite = (int)Math.Min(MaxBite, (long)_bite * BiteGrowthFactor);
        return _rendered;
    }
}
