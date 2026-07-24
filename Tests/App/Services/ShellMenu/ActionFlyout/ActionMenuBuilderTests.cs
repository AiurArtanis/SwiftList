using SwiftList.App.Services.ShellMenu.ActionFlyout;

namespace SwiftList.App.Tests.Services.ShellMenu.ActionFlyout;

[TestClass]
public sealed class ActionMenuBuilderTests
{
    private static ActionMenuItem Item(string text, bool hasSubMenu = false) => new() { Text = text, HasSubMenu = hasSubMenu };
    private static ActionMenuItem Separator() => new() { IsSeparator = true };
    private static ActionMenuItem Header(string title) => new() { IsSectionHeader = true, SectionTitle = title };

    [TestMethod]
    public void FinalizeItems_NoDuplicates_ReturnsAllUnchanged()
    {
        var items = new List<ActionMenuItem> { Item("Copy"), Item("Paste") };

        var result = ActionMenuBuilder.FinalizeItems(items);

        CollectionAssert.AreEqual(items, result);
    }

    [TestMethod]
    public void FinalizeItems_DuplicateText_KeepsFirstWhenNeitherHasSubMenu()
    {
        var first = Item("Open");
        var second = Item("Open");

        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { first, second });

        Assert.HasCount(1, result);
        Assert.AreSame(first, result[0]);
    }

    [TestMethod]
    public void FinalizeItems_DuplicateText_PrefersTheOneWithSubMenu()
    {
        var plain = Item("Send to");
        var withSubMenu = Item("Send to", hasSubMenu: true);

        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { plain, withSubMenu });

        Assert.HasCount(1, result);
        Assert.AreSame(withSubMenu, result[0]);
    }

    [TestMethod]
    public void FinalizeItems_DuplicateMatchIsCaseInsensitive()
    {
        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { Item("Copy"), Item("copy") });

        Assert.HasCount(1, result);
    }

    [TestMethod]
    public void FinalizeItems_ConsecutiveSeparators_CollapseToOne()
    {
        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { Item("A"), Separator(), Separator(), Item("B") });

        Assert.HasCount(3, result);
        Assert.IsTrue(result[1].IsSeparator);
        Assert.AreEqual("B", result[2].Text);
    }

    [TestMethod]
    public void FinalizeItems_LeadingSeparator_IsDropped()
    {
        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { Separator(), Item("A") });

        Assert.HasCount(1, result);
        Assert.AreEqual("A", result[0].Text);
    }

    [TestMethod]
    public void FinalizeItems_TrailingSeparator_IsDropped()
    {
        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { Item("A"), Separator() });

        Assert.HasCount(1, result);
        Assert.AreEqual("A", result[0].Text);
    }

    [TestMethod]
    public void FinalizeItems_SeparatorRightAfterHeader_IsDropped()
    {
        var result = ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem> { Header("Group"), Separator(), Item("A") });

        Assert.HasCount(2, result);
        Assert.IsTrue(result[0].IsSectionHeader);
        Assert.AreEqual("A", result[1].Text);
    }

    [TestMethod]
    public void FinalizeItems_EmptyList_ReturnsEmpty() =>
        Assert.IsEmpty(ActionMenuBuilder.FinalizeItems(new List<ActionMenuItem>()));
}
