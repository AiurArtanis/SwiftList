using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.Plugins.FolderCascader.Navigation;

namespace SwiftList.Plugins.FolderCascader.Tests;

[TestClass]
public sealed class MenuBuilderTests
{
    private static FolderCascaderPlugin.FolderConfigItem Folder(string name, string path, string subMenu = "") =>
        new() { Name = name, Path = path, SubMenu = subMenu };

    [TestMethod]
    public void SplitSubMenuPath_Empty_ReturnsEmptyArray() =>
        Assert.IsEmpty(MenuBuilder.SplitSubMenuPath(""));

    [TestMethod]
    public void SplitSubMenuPath_SingleSegment_ReturnsOneElement() =>
        CollectionAssert.AreEqual(new[] { "Tools" }, MenuBuilder.SplitSubMenuPath("Tools"));

    [TestMethod]
    public void SplitSubMenuPath_MultipleSegments_SplitsOnSlash() =>
        CollectionAssert.AreEqual(new[] { "Tools", "Network" }, MenuBuilder.SplitSubMenuPath("Tools/Network"));

    [TestMethod]
    public void SplitSubMenuPath_EmptySegmentsAndWhitespace_AreDropped() =>
        CollectionAssert.AreEqual(new[] { "Tools", "Network" }, MenuBuilder.SplitSubMenuPath(" Tools // Network /"));

    [TestMethod]
    public void StartsWithPrefix_EmptyPrefix_AlwaysMatches() =>
        Assert.IsTrue(MenuBuilder.StartsWithPrefix(new[] { "Tools", "Network" }, Array.Empty<string>()));

    [TestMethod]
    public void StartsWithPrefix_MatchingPrefix_ReturnsTrue() =>
        Assert.IsTrue(MenuBuilder.StartsWithPrefix(new[] { "Tools", "Network" }, new[] { "Tools" }));

    [TestMethod]
    public void StartsWithPrefix_NonMatchingPrefix_ReturnsFalse() =>
        Assert.IsFalse(MenuBuilder.StartsWithPrefix(new[] { "Tools", "Network" }, new[] { "Apps" }));

    [TestMethod]
    public void StartsWithPrefix_ShorterThanPrefix_ReturnsFalse() =>
        Assert.IsFalse(MenuBuilder.StartsWithPrefix(new[] { "Tools" }, new[] { "Tools", "Network" }));

    [TestMethod]
    public void StartsWithPrefix_IsCaseSensitive() =>
        Assert.IsFalse(MenuBuilder.StartsWithPrefix(new[] { "tools" }, new[] { "Tools" }));

    [TestMethod]
    public void EncodeThenDecodeCategoryPath_RoundTrips()
    {
        var encoded = MenuBuilder.EncodeCategoryPath(new[] { "Tools", "Network" });

        var decoded = MenuBuilder.TryDecodeCategoryPath(encoded, out var segments);

        Assert.IsTrue(decoded);
        CollectionAssert.AreEqual(new[] { "Tools", "Network" }, segments);
    }

    [TestMethod]
    public void TryDecodeCategoryPath_RealFilesystemPath_ReturnsFalse()
    {
        var decoded = MenuBuilder.TryDecodeCategoryPath(@"C:\some\path", out var segments);

        Assert.IsFalse(decoded);
        Assert.IsEmpty(segments);
    }

    [TestMethod]
    public void AddFolderItems_TopLevelFolder_AddsLeafItem()
    {
        var provider = new Provider();
        var items = new List<DynamicMenuItem>();
        var folders = new List<FolderCascaderPlugin.FolderConfigItem> { Folder("Downloads", @"C:\Downloads") };

        MenuBuilder.AddFolderItems(items, folders, Array.Empty<string>(), provider);

        Assert.HasCount(1, items);
        Assert.AreEqual("Downloads", items[0].Text);
        // A leaf item's SubMenuHandle is only ever allocated (non-zero) when the path actually exists
        // and HasSubMenu is set -- the two must never disagree.
        Assert.AreEqual(items[0].HasSubMenu, items[0].SubMenuHandle != IntPtr.Zero);
    }

    [TestMethod]
    public void AddFolderItems_NestedFolder_AddsOneCategoryEntryNotALeaf()
    {
        var provider = new Provider();
        var items = new List<DynamicMenuItem>();
        var folders = new List<FolderCascaderPlugin.FolderConfigItem>
        {
            Folder("Router UI", @"C:\Net\Router", "Tools/Network"),
        };

        MenuBuilder.AddFolderItems(items, folders, Array.Empty<string>(), provider);

        Assert.HasCount(1, items);
        Assert.AreEqual("Tools", items[0].Text);
        Assert.IsTrue(items[0].HasSubMenu);
    }

    [TestMethod]
    public void AddFolderItems_TwoFoldersSameCategory_YieldsOnlyOneCategoryEntry()
    {
        var provider = new Provider();
        var items = new List<DynamicMenuItem>();
        var folders = new List<FolderCascaderPlugin.FolderConfigItem>
        {
            Folder("A", @"C:\A", "Tools"),
            Folder("B", @"C:\B", "Tools"),
        };

        MenuBuilder.AddFolderItems(items, folders, Array.Empty<string>(), provider);

        Assert.HasCount(1, items);
        Assert.AreEqual("Tools", items[0].Text);
    }

    [TestMethod]
    public void AddFolderItems_ExpandingCategoryHandle_YieldsItsChildren()
    {
        var provider = new Provider();
        var rootItems = new List<DynamicMenuItem>();
        var folders = new List<FolderCascaderPlugin.FolderConfigItem>
        {
            Folder("Router UI", @"C:\Net\Router", "Tools/Network"),
            Folder("Ping Script", @"C:\Net\Ping", "Tools/Network"),
            Folder("Top-level", @"C:\Other"),
        };

        MenuBuilder.AddFolderItems(rootItems, folders, Array.Empty<string>(), provider);
        // rootItems: one "Tools" category entry + one real top-level leaf.
        Assert.HasCount(2, rootItems);
        var toolsHandle = rootItems.Single(i => i.Text == "Tools").SubMenuHandle;
        Assert.IsTrue(MenuBuilder.TryDecodeCategoryPath(GetPath(provider, toolsHandle), out var toolsPrefix));
        CollectionAssert.AreEqual(new[] { "Tools" }, toolsPrefix);

        var toolsChildren = new List<DynamicMenuItem>();
        MenuBuilder.AddFolderItems(toolsChildren, folders, toolsPrefix, provider);

        // "Tools" itself has no direct leaf at this level (both folders are one level deeper, under
        // "Network"), so expanding it yields exactly one further "Network" category, not the two leaves.
        Assert.HasCount(1, toolsChildren);
        Assert.AreEqual("Network", toolsChildren[0].Text);

        var networkChildren = new List<DynamicMenuItem>();
        MenuBuilder.AddFolderItems(networkChildren, folders, new[] { "Tools", "Network" }, provider);

        Assert.HasCount(2, networkChildren);
        CollectionAssert.AreEquivalent(new[] { "Router UI", "Ping Script" }, networkChildren.Select(i => i.Text).ToList());
    }

    [TestMethod]
    public void AddFolderItems_SeparatorAtMatchingLevel_AddsSeparator()
    {
        var provider = new Provider();
        var items = new List<DynamicMenuItem>();
        var folders = new List<FolderCascaderPlugin.FolderConfigItem>
        {
            Folder("A", @"C:\A"),
            Folder("-", "-"),
            Folder("B", @"C:\B"),
        };

        MenuBuilder.AddFolderItems(items, folders, Array.Empty<string>(), provider);

        Assert.HasCount(3, items);
        Assert.IsTrue(items[1].IsSeparator);
    }

    private static string GetPath(Provider provider, IntPtr handle)
    {
        Assert.IsTrue(provider.TryGetPath(handle, out var path));
        return path!;
    }
}
