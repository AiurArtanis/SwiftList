using SwiftList.App.Helpers;

namespace SwiftList.App.Tests.Helpers;

[TestClass]
public sealed class ObservableRangeCollectionTests
{
    [TestMethod]
    public void ReplaceRange_ReplacesAllItems()
    {
        var collection = new ObservableRangeCollection<int> { 1, 2, 3 };

        collection.ReplaceRange(new[] { 4, 5 });

        CollectionAssert.AreEqual(new[] { 4, 5 }, collection);
    }

    [TestMethod]
    public void ReplaceRange_RaisesSingleResetNotification()
    {
        var collection = new ObservableRangeCollection<int> { 1 };
        var resetCount = 0;
        collection.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                resetCount++;
        };

        collection.ReplaceRange(new[] { 2, 3 });

        Assert.AreEqual(1, resetCount);
    }

    [TestMethod]
    public void ReplaceRange_NullCollection_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => new ObservableRangeCollection<int>().ReplaceRange(null!));

    [TestMethod]
    public void ReplaceRange_EmptySource_ClearsCollection()
    {
        var collection = new ObservableRangeCollection<int> { 1, 2 };

        collection.ReplaceRange(Array.Empty<int>());

        Assert.IsEmpty(collection);
    }

    [TestMethod]
    public void ReconcileTo_SameLength_ReplacesOnlyDifferingItems()
    {
        var collection = new ObservableRangeCollection<string> { "a", "b", "c" };
        var replaced = new List<int>();
        collection.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
                replaced.Add(e.NewStartingIndex);
        };

        collection.ReconcileTo(new[] { "a", "X", "c" }, (x, y) => x == y);

        CollectionAssert.AreEqual(new[] { "a", "X", "c" }, collection);
        CollectionAssert.AreEqual(new[] { 1 }, replaced);
    }

    [TestMethod]
    public void ReconcileTo_TargetLonger_AppendsRemainder()
    {
        var collection = new ObservableRangeCollection<int> { 1, 2 };

        collection.ReconcileTo(new[] { 1, 2, 3, 4 }, (x, y) => x == y);

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, collection);
    }

    // Granular notifications are the point of ReconcileTo, but they are per row, and an ItemsControl has
    // to process every one whether or not virtualization spares it the rendering. A result set of
    // hundreds of thousands therefore froze the window mid-append and looked like results never arriving.
    [TestMethod]
    public void ReconcileTo_ALargeAppend_RaisesOneNotificationRatherThanOnePerRow()
    {
        var collection = new ObservableRangeCollection<int> { 1, 2, 3 };
        var events = 0;
        collection.CollectionChanged += (_, _) => events++;

        collection.ReconcileTo(Enumerable.Range(1, 50_000).ToList(), (x, y) => x == y);

        Assert.HasCount(50_000, collection);
        Assert.AreEqual(1, events, "a bulk append must not notify per row");
    }

    [TestMethod]
    public void ReconcileTo_ALargeTrim_AlsoCollapsesToOneNotification()
    {
        var collection = new ObservableRangeCollection<int>();
        collection.ReplaceRange(Enumerable.Range(1, 50_000));
        var events = 0;
        collection.CollectionChanged += (_, _) => events++;

        collection.ReconcileTo(new[] { 1, 2, 3 }, (x, y) => x == y);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, collection);
        Assert.AreEqual(1, events);
    }

    [TestMethod]
    public void ReconcileTo_AnOrdinarySizedChange_StaysGranular()
    {
        // The flicker-free path has to survive: a Reset makes WPF discard and rebuild every container
        // from the top, which is what row-by-row updates exist to avoid for a normal keystroke.
        var collection = new ObservableRangeCollection<int>();
        collection.ReplaceRange(Enumerable.Range(1, 100));
        var resets = 0;
        collection.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                resets++;
        };

        collection.ReconcileTo(Enumerable.Range(1, 140).ToList(), (x, y) => x == y);

        Assert.HasCount(140, collection);
        Assert.AreEqual(0, resets, "a change this size must stay row-by-row");
    }

    [TestMethod]
    public void ReconcileTo_TargetShorter_TrimsTail()
    {
        var collection = new ObservableRangeCollection<int> { 1, 2, 3, 4 };

        collection.ReconcileTo(new[] { 1, 2 }, (x, y) => x == y);

        CollectionAssert.AreEqual(new[] { 1, 2 }, collection);
    }

    [TestMethod]
    public void ReconcileTo_IdenticalTarget_RaisesNoNotifications()
    {
        var collection = new ObservableRangeCollection<int> { 1, 2, 3 };
        var raised = false;
        collection.CollectionChanged += (_, _) => raised = true;

        collection.ReconcileTo(new[] { 1, 2, 3 }, (x, y) => x == y);

        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void LastUpdateExtendedContent_DefaultsToFalse() =>
        Assert.IsFalse(new ObservableRangeCollection<int>().LastUpdateExtendedContent);

    [TestMethod]
    public void ReconcileTo_ExtendingContent_ReportsItOnTheGranularPath()
    {
        var collection = new ObservableRangeCollection<int> { 1, 2 };

        collection.ReconcileTo(new[] { 1, 2, 3 }, (x, y) => x == y, extendsContent: true);

        Assert.IsTrue(collection.LastUpdateExtendedContent);
    }

    [TestMethod]
    public void ReconcileTo_ExtendingContent_SurvivesTheResetPath()
    {
        // The whole point of the flag is the large-jump case -- a progressive render growing 2k rows to
        // 8k is exactly the update that takes the Reset shortcut. ReplaceRange clears the flag on its
        // own (a direct caller is replacing content, not extending it), so a shared implementation
        // would report false for precisely the updates that need true.
        var collection = new ObservableRangeCollection<int>();
        collection.ReplaceRange(Enumerable.Range(1, 2_000));

        collection.ReconcileTo(Enumerable.Range(1, 8_000).ToList(), (x, y) => x == y, extendsContent: true);

        Assert.IsTrue(collection.LastUpdateExtendedContent);
    }

    [TestMethod]
    public void ReconcileTo_WithoutTheFlag_ReportsAReplacement()
    {
        var collection = new ObservableRangeCollection<int> { 1, 2 };
        collection.ReconcileTo(new[] { 1, 2, 3 }, (x, y) => x == y, extendsContent: true);

        collection.ReconcileTo(new[] { 9, 8 }, (x, y) => x == y);

        Assert.IsFalse(collection.LastUpdateExtendedContent);
    }

    [TestMethod]
    public void ReplaceRange_ClearsAPreviousExtendedContentFlag()
    {
        var collection = new ObservableRangeCollection<int> { 1, 2 };
        collection.ReconcileTo(new[] { 1, 2, 3 }, (x, y) => x == y, extendsContent: true);

        collection.ReplaceRange(new[] { 7 });

        Assert.IsFalse(collection.LastUpdateExtendedContent);
    }

    [TestMethod]
    public void ReconcileTo_NullTarget_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => new ObservableRangeCollection<int>().ReconcileTo(null!, (x, y) => x == y));

    [TestMethod]
    public void ReconcileTo_NullEquals_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => new ObservableRangeCollection<int> { 1 }.ReconcileTo(new[] { 1 }, null!));
}
