using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Models;
using SwiftList.PluginSdk.Services;
using SwiftList.Plugins.FolderCascader.Navigation;

namespace SwiftList.Plugins.FolderCascader.Tests;

// Some tests wire PluginSettingsService.GetSettingFunc/SetSettingFunc/PluginPromptService.PromptFunc/
// FavoritesService.GetFavoritesFunc/HistoryService.GetHistoryEntriesFunc (shared static delegates) --
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
    public void GetMenuItems_RootLevel_NeverIncludesAHeaderOrStandaloneAddItem()
    {
        // Root's own "+" comes from Provider.HeaderAction, rendered by the host directly into the
        // group header row (see QuickNavigationMenu.Show) -- it's never one of GetMenuItems' own
        // returned DynamicMenuItems the way a category submenu's header is.
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "SwiftList.Plugins.FolderCascader" && key == "Folders"
                ? new List<FolderCascaderPlugin.FolderConfigItem> { Folder("Downloads", @"C:\Downloads") }
                : defaultValue;
        try
        {
            var provider = new Provider();
            var result = new FakeResult { FullPath = Path.GetTempPath() };

            var items = MenuBuilder.GetMenuItems(result, IntPtr.Zero, provider).ToList();

            Assert.IsFalse(items.Any(i => i.IsHeader));
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
        }
    }

    [TestMethod]
    public void Provider_HeaderAction_IsWiredWithATooltip()
    {
        var provider = new Provider();

        Assert.IsNotNull(provider.HeaderAction);
        Assert.IsFalse(string.IsNullOrEmpty(provider.HeaderActionTooltip));
    }

    [TestMethod]
    public void Provider_HeaderAction_PromptsThenSavesAtRootLevel()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "SwiftList.Plugins.FolderCascader" && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (_, _, value) => saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;
        PluginPromptService.PromptFunc = (title, fields, initialValues) =>
            fields.ToDictionary(f => f.Key, object? (f) => f.DefaultValue);
        try
        {
            var provider = new Provider();
            var result = new FakeResult { FullPath = Path.GetTempPath() };

            provider.HeaderAction!(result);

            var added = saved!.Single();
            Assert.AreEqual(Path.GetTempPath(), added.Path);
            Assert.AreEqual("", added.SubMenu);
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
            PluginSettingsService.SetSettingFunc = null;
            PluginPromptService.PromptFunc = null;
        }
    }

    [TestMethod]
    public void GetMenuItems_CategoryLevel_FirstItemIsHeaderNamedAfterTheCategorysLastSegment()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "SwiftList.Plugins.FolderCascader" && key == "Folders"
                ? new List<FolderCascaderPlugin.FolderConfigItem> { Folder("Router UI", @"C:\Net\Router", "Tools/Network") }
                : defaultValue;
        try
        {
            var provider = new Provider();
            var handle = provider.AllocateHandle(MenuBuilder.EncodeCategoryPath(new[] { "Tools", "Network" }));
            var result = new FakeResult { FullPath = Path.GetTempPath() };

            var items = MenuBuilder.GetMenuItems(result, handle, provider).ToList();

            var header = items[0];
            Assert.IsTrue(header.IsHeader);
            Assert.AreEqual("Network", header.Text);
            Assert.IsNotNull(header.OnExecute);
            Assert.IsTrue(items.Any(i => i.Text == "Router UI"));
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
        }
    }

    [TestMethod]
    public void GetMenuItems_EmptyCategoryLevel_StillGetsAHeader()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "SwiftList.Plugins.FolderCascader" && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        try
        {
            var provider = new Provider();
            var handle = provider.AllocateHandle(MenuBuilder.EncodeCategoryPath(new[] { "Empty" }));
            var result = new FakeResult { FullPath = Path.GetTempPath() };

            var items = MenuBuilder.GetMenuItems(result, handle, provider).ToList();

            Assert.IsTrue(items[0].IsHeader);
            Assert.AreEqual("Empty", items[0].Text);
            Assert.IsTrue(items.Any(i => i.IsDisabled));
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
        }
    }

    [TestMethod]
    public void GetMenuItems_CategoryLevel_HeaderOnExecute_PromptsThenSavesAtThatSubMenu()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "SwiftList.Plugins.FolderCascader" && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (_, _, value) => saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;
        PluginPromptService.PromptFunc = (title, fields, initialValues) =>
            fields.ToDictionary(f => f.Key, object? (f) => f.DefaultValue);
        try
        {
            var provider = new Provider();
            var handle = provider.AllocateHandle(MenuBuilder.EncodeCategoryPath(new[] { "Tools", "Network" }));
            var result = new FakeResult { FullPath = Path.GetTempPath() };
            var items = MenuBuilder.GetMenuItems(result, handle, provider).ToList();

            items[0].OnExecute!();

            Assert.AreEqual("Tools/Network", saved!.Single().SubMenu);
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
            PluginSettingsService.SetSettingFunc = null;
            PluginPromptService.PromptFunc = null;
        }
    }

    [TestMethod]
    public void PromptAndAddCurrentFolder_PromptsWithAllThreeFieldsPreFilled()
    {
        using var dir = new TempDirectory();
        var subDir = Directory.CreateDirectory(Path.Combine(dir.Path, "MyStuff"));
        IReadOnlyList<PluginConfigField>? promptedFields = null;
        PluginPromptService.PromptFunc = (title, fields, initialValues) =>
        {
            promptedFields = fields;
            return null; // cancel -- this test only cares about what was asked, not the save
        };
        try
        {
            MenuBuilder.PromptAndAddCurrentFolder(subDir.FullName, "Tools/Network");

            Assert.IsNotNull(promptedFields);
            Assert.HasCount(3, promptedFields);
            var nameField = promptedFields.Single(f => f.Key == "Name");
            Assert.AreEqual(ConfigFieldType.Text, nameField.FieldType);
            Assert.AreEqual("MyStuff", nameField.DefaultValue);

            var pathField = promptedFields.Single(f => f.Key == "Path");
            Assert.AreEqual(ConfigFieldType.FolderPath, pathField.FieldType);
            Assert.AreEqual(subDir.FullName, pathField.DefaultValue);

            var subMenuField = promptedFields.Single(f => f.Key == "SubMenu");
            Assert.AreEqual(ConfigFieldType.Text, subMenuField.FieldType);
            Assert.AreEqual("Tools/Network", subMenuField.DefaultValue);
        }
        finally
        {
            PluginPromptService.PromptFunc = null;
        }
    }

    [TestMethod]
    public void PromptAndAddCurrentFolder_PromptConfirmed_SavesEnteredNamePathAndSubMenu()
    {
        using var dir = new TempDirectory();
        using var editedDir = new TempDirectory();
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "SwiftList.Plugins.FolderCascader" && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (_, _, value) => saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;
        PluginPromptService.PromptFunc = (title, fields, initialValues) =>
            new Dictionary<string, object?> { ["Name"] = "Custom Name", ["Path"] = editedDir.Path, ["SubMenu"] = "NewCategory" };
        try
        {
            MenuBuilder.PromptAndAddCurrentFolder(dir.Path, "");

            var added = saved!.Single();
            Assert.AreEqual("Custom Name", added.Name);
            Assert.AreEqual(editedDir.Path, added.Path);
            Assert.AreEqual("NewCategory", added.SubMenu);
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
            PluginSettingsService.SetSettingFunc = null;
            PluginPromptService.PromptFunc = null;
        }
    }

    [TestMethod]
    public void PromptAndAddCurrentFolder_EditedPathClearedToBlank_FallsBackToOriginalFolder()
    {
        using var dir = new TempDirectory();
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == "SwiftList.Plugins.FolderCascader" && key == "Folders" ? new List<FolderCascaderPlugin.FolderConfigItem>() : defaultValue;
        List<FolderCascaderPlugin.FolderConfigItem>? saved = null;
        PluginSettingsService.SetSettingFunc = (_, _, value) => saved = (List<FolderCascaderPlugin.FolderConfigItem>)value!;
        PluginPromptService.PromptFunc = (title, fields, initialValues) =>
            new Dictionary<string, object?> { ["Name"] = "", ["Path"] = "   ", ["SubMenu"] = "" };
        try
        {
            MenuBuilder.PromptAndAddCurrentFolder(dir.Path, "");

            Assert.AreEqual(dir.Path, saved!.Single().Path);
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = null;
            PluginSettingsService.SetSettingFunc = null;
            PluginPromptService.PromptFunc = null;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("swiftlist-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
