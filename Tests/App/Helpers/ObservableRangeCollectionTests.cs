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
    public void ReconcileTo_NullTarget_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => new ObservableRangeCollection<int>().ReconcileTo(null!, (x, y) => x == y));

    [TestMethod]
    public void ReconcileTo_NullEquals_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => new ObservableRangeCollection<int> { 1 }.ReconcileTo(new[] { 1 }, null!));
}
