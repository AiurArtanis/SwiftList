using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Models;
using SwiftList.PluginSdk.Services;
using SwiftList.Plugins.FolderCascader.Navigation;

namespace SwiftList.Plugins.FolderCascader.Tests;

// Some tests wire PluginSettingsService.GetSettingFunc/SetSettingFunc/FavoritesService.
// GetFavoritesFunc/HistoryService.GetHistoryEntriesFunc (shared static delegates) --
// [DoNotParallelize] keeps it from racing against other tests in this class touching the same statics.
[TestClass]
[DoNotParallelize]
public sealed class MenuBuilderTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

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
        // QuickNavigationMenu's own click-suppression for HasSubMenu items only applies automatically
        // at the root level (isRootItem) -- nested submenu levels rely entirely on this flag, so a
        // category item must set it explicitly regardless of how deep it sits, or clicking it (rather
        // than hovering to expand) fires as if it were a real actionable leaf.
        Assert.IsFalse(items[0].IsActionable);
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

    [TestMethod]
    public void AppendAddCurrentFolderItem_ExistingDirectory_AppendsItemWithOnExecuteNotCommandId()
    {
        var items = new List<DynamicMenuItem>();
        var result = new FakeResult { FullPath = Path.GetTempPath() };

        MenuBuilder.AppendAddCurrentFolderItem(items, result, Array.Empty<string>());

        var added = items.Single();
        Assert.IsNotNull(added.OnExecute);
        // Must NOT use CommandId: the host resolves any allocated CommandId straight to its stored
        // string and passes that to NavigateOrOpen as a literal path to shell-open (see
        // QuickNavigationMenu.CreateMenuItem), before Provider.ExecuteCommand ever runs.
        Assert.AreEqual(0u, added.CommandId);
    }

    [TestMethod]
    public void AppendAddCurrentFolderItem_OnExecute_SavesTheActiveFolderAtTheGivenLevel()
    {
        PluginSdk.Services.PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "SwiftList.Plugins.FolderCascader" && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSdk.Services.PluginSettingsService.SetSettingFunc = (_, _, value) => saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;
        try
        {
            var items = new List<DynamicMenuItem>();
            var result = new FakeResult { FullPath = Path.GetTempPath() };
            MenuBuilder.AppendAddCurrentFolderItem(items, result, new[] { "Tools", "Network" });

            items.Single().OnExecute!();

            var added = saved!.Single();
            Assert.AreEqual(Path.GetTempPath(), added.Path);
            Assert.AreEqual("Tools/Network", added.SubMenu);
        }
        finally
        {
            PluginSdk.Services.PluginSettingsService.GetSettingFunc = null;
            PluginSdk.Services.PluginSettingsService.SetSettingFunc = null;
        }
    }

    [TestMethod]
    public void AppendAddCurrentFolderItem_NonExistentDirectory_AddsNothing()
    {
        var items = new List<DynamicMenuItem>();
        var result = new FakeResult { FullPath = @"Z:\definitely-not-a-real-swiftlist-dir" };

        MenuBuilder.AppendAddCurrentFolderItem(items, result, Array.Empty<string>());

        Assert.IsEmpty(items);
    }

    [TestMethod]
    public void AppendAddCurrentFolderItem_EmptyFullPath_AddsNothing()
    {
        var items = new List<DynamicMenuItem>();
        var result = new FakeResult { FullPath = "" };

        MenuBuilder.AppendAddCurrentFolderItem(items, result, Array.Empty<string>());

        Assert.IsEmpty(items);
    }

    [TestMethod]
    public void AppendAddCurrentFolderItem_NonEmptyItemsWithoutTrailingSeparator_InsertsSeparatorFirst()
    {
        var items = new List<DynamicMenuItem> { new() { Text = "Existing" } };
        var result = new FakeResult { FullPath = Path.GetTempPath() };

        MenuBuilder.AppendAddCurrentFolderItem(items, result, Array.Empty<string>());

        Assert.HasCount(3, items);
        Assert.IsTrue(items[1].IsSeparator);
    }

    [TestMethod]
    public void GetMenuItems_RootLevel_AddCurrentFolderComesBeforeFavoritesAndHistory()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
        {
            if (pluginId != "SwiftList.Plugins.FolderCascader") return defaultValue;
            return key switch
            {
                "Folders" => new List<FolderCascaderPlugin.FolderConfigItem> { Folder("Downloads", @"C:\Downloads") },
                "ShowFavorites" => true,
                "ShowHistory" => true,
                _ => defaultValue
            };
        };
        FavoritesService.GetFavoritesFunc = () => new[] { new FavoriteItem { Name = "MyFav", Path = @"C:\Fav" } };
        HistoryService.GetHistoryEntriesFunc = () => new[] { new HistoryEntry("", @"C:\Hist", HistoryEntryKind.Folder, 0) };
        try
        {
            var provider = new Provider();
            var result = new FakeResult { FullPath = Path.GetTempPath() };

            var items = MenuBuilder.GetMenuItems(result, IntPtr.Zero, provider).ToList();

            var addText = TranslationService.Get("FolderCascader_AddCurrentFolder");
            var favoritesText = TranslationService.Get("FolderCascader_Favorites");
            var addIndex = items.FindIndex(i => i.Text == addText);
            var favoritesIndex = items.FindIndex(i => i.Text == favoritesText);
            Assert.IsGreaterThanOrEqualTo(0, addIndex, "Add Current Folder item should be present");
            Assert.IsGreaterThanOrEqualTo(0, favoritesIndex, "Favorites item should be present");
            Assert.IsLessThan(favoritesIndex, addIndex, "Add Current Folder must come before Favorites/History, not after");
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
            FavoritesService.GetFavoritesFunc = null;
            HistoryService.GetHistoryEntriesFunc = null;
        }
    }
}
