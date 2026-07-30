using SwiftList.Core.Services.Search;

namespace SwiftList.Core.Tests.Services.Search;

// After a whole-drive query the service holds one SearchResult per match plus the strings in it, all
// unreachable once written to the pipe, and then allocates nothing at all while it waits for the next
// request -- so nothing provokes a collection and the working set stays where the biggest search left
// it. The pump asks for one explicitly, but only when the search was big enough to be worth a gen2
// pause. What matters here is where that line sits, which is a promise about ordinary typing.
[TestClass]
public sealed class SearchStreamPumpReclaimTests
{
    [TestMethod]
    public void AnOrdinaryKeystroke_DoesNotReclaim()
    {
        // The quick window asks for 51 and the full window's own paints settle in the low thousands;
        // none of that leaves behind enough to justify stopping the world for it.
        Assert.IsFalse(SearchStreamPump.ShouldReclaimAfter(0));
        Assert.IsFalse(SearchStreamPump.ShouldReclaimAfter(51));
        Assert.IsFalse(SearchStreamPump.ShouldReclaimAfter(1_000));
        Assert.IsFalse(SearchStreamPump.ShouldReclaimAfter(50_000));
    }

    [TestMethod]
    public void AWholeDriveQuery_Reclaims()
    {
        Assert.IsTrue(SearchStreamPump.ShouldReclaimAfter(100_000));
        Assert.IsTrue(SearchStreamPump.ShouldReclaimAfter(660_000));
    }

    [TestMethod]
    public void ASearchThatStreamedNothing_DoesNotReclaim()
    {
        // A cancelled or failed search never records a count, so it reads as zero here. Superseded
        // typing is exactly when a gen2 pause would be felt, and the next query's own garbage will
        // trigger one soon enough anyway.
        Assert.IsFalse(SearchStreamPump.ShouldReclaimAfter(0));
    }
}
