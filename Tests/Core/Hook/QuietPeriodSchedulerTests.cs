using SwiftList.Core.Hook;

namespace SwiftList.Core.Tests.Hook;

/// <summary>
/// The scheduler exists to keep a burst of window-movement events from turning into a burst of blocking
/// cross-process calls, so what is asserted here is how many times the work actually runs.
/// </summary>
[TestClass]
public class QuietPeriodSchedulerTests
{
    private const int QuietMs = 40;

    // Comfortably longer than the quiet period, so a deferred run has certainly happened by the time it is
    // asserted on. Kept off the critical path of what is being measured -- these only ever wait, never race.
    private const int SettleMs = 400;

    [TestMethod]
    public void RunsImmediatelyWhenAskedTo()
    {
        var runs = 0;
        using var scheduler = new QuietPeriodScheduler(() => Interlocked.Increment(ref runs), QuietMs);

        scheduler.RunNow();

        Assert.AreEqual(1, runs);
    }

    [TestMethod]
    public void DoesNotRunAtOnceWhenDeferred()
    {
        var runs = 0;
        using var scheduler = new QuietPeriodScheduler(() => Interlocked.Increment(ref runs), QuietMs);

        scheduler.RunWhenQuiet();

        Assert.AreEqual(0, runs);
    }

    [TestMethod]
    public void ABurstOfDeferredRequestsProducesASingleRun()
    {
        // The whole point: resizing a window emitted ~200 location changes a second, each of which used to
        // poll the tracked window over a synchronous cross-process call.
        var runs = 0;
        using var scheduler = new QuietPeriodScheduler(() => Interlocked.Increment(ref runs), QuietMs);

        for (var i = 0; i < 200; i++)
            scheduler.RunWhenQuiet();

        Assert.AreEqual(0, runs, "nothing should run while the requests are still arriving");
        Thread.Sleep(SettleMs);
        Assert.AreEqual(1, runs);
    }

    [TestMethod]
    public void ADeferredRunHappensOnceTheBurstStops()
    {
        var runs = 0;
        using var scheduler = new QuietPeriodScheduler(() => Interlocked.Increment(ref runs), QuietMs);

        scheduler.RunWhenQuiet();
        Thread.Sleep(SettleMs);

        Assert.AreEqual(1, runs);
    }

    [TestMethod]
    public void AnImmediateRunDropsAPendingDeferredOne()
    {
        // It answers whatever the pending one was going to ask, so letting it fire too would be one more
        // trip into the tracked window for nothing.
        var runs = 0;
        using var scheduler = new QuietPeriodScheduler(() => Interlocked.Increment(ref runs), QuietMs);

        scheduler.RunWhenQuiet();
        scheduler.RunNow();
        Thread.Sleep(SettleMs);

        Assert.AreEqual(1, runs);
    }

    [TestMethod]
    public void CancelDropsAPendingDeferredRun()
    {
        var runs = 0;
        using var scheduler = new QuietPeriodScheduler(() => Interlocked.Increment(ref runs), QuietMs);

        scheduler.RunWhenQuiet();
        scheduler.Cancel();
        Thread.Sleep(SettleMs);

        Assert.AreEqual(0, runs);
    }

    [TestMethod]
    public void RunsNeverOverlap()
    {
        // Deferred runs arrive on a timer thread while immediate ones stay on the caller's, which is a new
        // way for two to meet -- the work used to be single-threaded.
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var concurrent = 0;
        var maxConcurrent = 0;

        using var scheduler = new QuietPeriodScheduler(() =>
        {
            var now = Interlocked.Increment(ref concurrent);
            InterlockedMax(ref maxConcurrent, now);
            entered.Set();
            release.Wait(SettleMs);
            Interlocked.Decrement(ref concurrent);
        }, QuietMs);

        var blocker = Task.Run(scheduler.RunNow);
        Assert.IsTrue(entered.Wait(SettleMs), "the first run never started");

        scheduler.RunNow(); // must not join the run already in progress
        release.Set();
        blocker.Wait(SettleMs * 2);

        Assert.AreEqual(1, maxConcurrent);
    }

    [TestMethod]
    public void ARunSkippedForBeingBusyIsRetried()
    {
        // Skipping without a retry would silently drop the refresh that request was asking for.
        var release = new ManualResetEventSlim();
        var runs = 0;
        var firstRun = new ManualResetEventSlim();

        using var scheduler = new QuietPeriodScheduler(() =>
        {
            if (Interlocked.Increment(ref runs) == 1)
            {
                firstRun.Set();
                release.Wait(SettleMs);
            }
        }, QuietMs);

        var blocker = Task.Run(scheduler.RunNow);
        Assert.IsTrue(firstRun.Wait(SettleMs), "the first run never started");

        scheduler.RunNow();       // skipped: the first is still holding the lock
        Assert.AreEqual(1, runs);

        release.Set();
        blocker.Wait(SettleMs * 2);
        Thread.Sleep(SettleMs);   // the skipped request re-armed itself

        Assert.AreEqual(2, runs);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value)
        {
            if (Interlocked.CompareExchange(ref target, value, seen) == seen)
                return;
        }
    }
}
