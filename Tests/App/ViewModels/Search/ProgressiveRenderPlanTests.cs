using SwiftList.App.ViewModels.Search;

namespace SwiftList.App.Tests.ViewModels.Search;

[TestClass]
public sealed class ProgressiveRenderPlanTests
{
    [TestMethod]
    public void BelowTheFirstRenderThreshold_PaintsNothing()
    {
        var plan = new ProgressiveRenderPlan();

        Assert.AreEqual(0, plan.NextRenderSize(ProgressiveRenderPlan.MinimumFirstRender - 1));
        Assert.AreEqual(0, plan.Rendered);
    }

    [TestMethod]
    public void AtTheFirstRenderThreshold_PaintsEverythingReceived()
    {
        var plan = new ProgressiveRenderPlan();

        Assert.AreEqual(ProgressiveRenderPlan.MinimumFirstRender, plan.NextRenderSize(ProgressiveRenderPlan.MinimumFirstRender));
    }

    [TestMethod]
    public void TheThresholdGatesOnlyTheFirstPaint()
    {
        // A search that trickles must still be able to paint its 10th, 11th, ... result once it has
        // cleared the threshold once -- the gate exists to avoid painting a two-row list that's about
        // to be superseded, not to impose a floor on every later render.
        var plan = new ProgressiveRenderPlan();
        plan.NextRenderSize(20);

        Assert.AreEqual(21, plan.NextRenderSize(21));
    }

    [TestMethod]
    public void AFirstPaintTakesOneBiteEvenWhenEverythingHasAlreadyArrived()
    {
        // A search resolving faster than the first 40ms tick must not turn that tick into a render of
        // the entire result set -- the first paint is the one that has to be immediate.
        var plan = new ProgressiveRenderPlan();

        Assert.AreEqual(ProgressiveRenderPlan.InitialBite, plan.NextRenderSize(5_000_000));
    }

    [TestMethod]
    public void TheBiteGrowsGeometricallyThenHoldsAtItsMaximum()
    {
        var plan = new ProgressiveRenderPlan();
        var totals = new List<int>();
        for (var i = 0; i < 5; i++)
            totals.Add(plan.NextRenderSize(5_000_000));

        var first = ProgressiveRenderPlan.InitialBite;
        var second = first * ProgressiveRenderPlan.BiteGrowthFactor;
        var third = second * ProgressiveRenderPlan.BiteGrowthFactor;
        CollectionAssert.AreEqual(
            new[]
            {
                first,
                first + second,
                first + second + third,
                first + second + third + ProgressiveRenderPlan.MaxBite,
                first + second + third + ProgressiveRenderPlan.MaxBite * 2,
            },
            totals);
    }

    [TestMethod]
    public void TheBiteNeverExceedsItsMaximum()
    {
        var plan = new ProgressiveRenderPlan();
        for (var i = 0; i < 20; i++)
            plan.NextRenderSize(50_000_000);

        Assert.AreEqual(ProgressiveRenderPlan.MaxBite, plan.Bite);
    }

    [TestMethod]
    public void TheTotalKeepsClimbingForAsLongAsResultsRemain()
    {
        // The behaviour this class was rewritten for. Capping the TOTAL made the list stop dead at a
        // round number and sit there for the rest of a multi-second search, which reads as a hang --
        // only the size of each step is capped, never how far the list is allowed to get.
        var plan = new ProgressiveRenderPlan();
        const int received = 3_000_000;
        var ticks = 0;
        while (plan.NextRenderSize(received) != 0)
        {
            ticks++;
            Assert.IsLessThan(500, ticks, "the plan must converge, not tick forever");
        }

        Assert.AreEqual(received, plan.Rendered);
    }

    [TestMethod]
    public void OnceEverythingReceivedIsPainted_FurtherTicksPaintNothing()
    {
        var plan = new ProgressiveRenderPlan();
        Assert.AreEqual(50, plan.NextRenderSize(50));

        Assert.AreEqual(0, plan.NextRenderSize(50));
    }

    [TestMethod]
    public void ASkippedTickDoesNotConsumeBiteGrowth()
    {
        // A skipped tick must leave the plan exactly where it was -- otherwise a slow stream, which
        // produces many nothing-new ticks, would burn through the whole growth ramp while still showing
        // a handful of rows, and the ramp would be spent by the time results actually arrived.
        var plan = new ProgressiveRenderPlan();
        plan.NextRenderSize(50);
        var biteAfterFirst = plan.Bite;

        plan.NextRenderSize(50);
        plan.NextRenderSize(50);

        Assert.AreEqual(biteAfterFirst, plan.Bite);
        Assert.AreEqual(50, plan.Rendered);
    }

    [TestMethod]
    public void PaintsOnlyWhatHasActuallyArrived()
    {
        var plan = new ProgressiveRenderPlan();

        Assert.AreEqual(300, plan.NextRenderSize(300));
        Assert.AreEqual(900, plan.NextRenderSize(900));
    }

    [TestMethod]
    public void AHugeBacklogDoesNotOverflowTheRunningTotal()
    {
        var plan = new ProgressiveRenderPlan();
        for (var i = 0; i < 30; i++)
            plan.NextRenderSize(int.MaxValue);

        Assert.IsGreaterThan(0, plan.Rendered);
    }
}
