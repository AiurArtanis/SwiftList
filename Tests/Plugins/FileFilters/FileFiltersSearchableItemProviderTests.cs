using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.FileFilters.Tests;

// PluginSettingsService.GetSettingFunc is a shared static delegate read once, synchronously, inside the
// constructor -- unlike the *InstantProvider classes seen elsewhere, there's no separate static cache to
// bust, but [DoNotParallelize] plus resetting in TestCleanup still keeps tests in this class from racing
// on the delegate itself.
[TestClass]
[DoNotParallelize]
public sealed class FileFiltersSearchableItemProviderTests
{
    private const string PluginId = "SwiftList.Plugins.FileFilters";

    [TestCleanup]
    public void Reset() => PluginSettingsService.GetSettingFunc = null;

    private static void ConfigureFilters(List<FileFiltersSearchableItemProvider.FilterItem> filters) =>
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == PluginId && key == "Filters" ? filters : defaultValue;

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("swiftlist-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void GetSearchableItems_NoConfiguredFilters_ReturnsEmpty()
    {
        using var provider = new FileFiltersSearchableItemProvider();

        Assert.IsEmpty(provider.GetSearchableItems());
    }

    [TestMethod]
    public void GetSearchableItems_DisabledFilter_IsExcluded()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "x");
        ConfigureFilters(new() { new() { Enabled = false, Folders = { dir.Path }, FilterPattern = "*.txt" } });

        using var provider = new FileFiltersSearchableItemProvider();

        Assert.IsEmpty(provider.GetSearchableItems());
    }

    [TestMethod]
    public void GetSearchableItems_MatchingFile_IsIncluded()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "video.mp4"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "readme.txt"), "x");
        ConfigureFilters(new() { new() { Enabled = true, Folders = { dir.Path }, FilterPattern = "*.mp4" } });

        using var provider = new FileFiltersSearchableItemProvider();
        var items = provider.GetSearchableItems().ToList();

        Assert.IsTrue(items.Any(i => i.Title == "video.mp4"));
        Assert.IsFalse(items.Any(i => i.Title == "readme.txt"));
    }

    [TestMethod]
    public void GetSearchableItems_Subdirectories_AreIncludedRegardlessOfFilterPattern()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, "Subfolder"));
        ConfigureFilters(new() { new() { Enabled = true, Folders = { dir.Path }, FilterPattern = "*.mp4" } });

        using var provider = new FileFiltersSearchableItemProvider();
        var items = provider.GetSearchableItems().ToList();

        Assert.IsTrue(items.Any(i => i.Title == "Subfolder" && i.ResultKind == "Directory"));
    }

    [TestMethod]
    public void GetSearchableItems_FilterName_IsPrefixedInDescription()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "a.mp4"), "x");
        ConfigureFilters(new() { new() { Enabled = true, Name = "Movies", Folders = { dir.Path }, FilterPattern = "*.mp4" } });

        using var provider = new FileFiltersSearchableItemProvider();
        var item = provider.GetSearchableItems().Single(i => i.Title == "a.mp4");

        Assert.StartsWith("Movies · ", item.Description);
    }

    [TestMethod]
    public void GetSearchableItems_FilterKeyword_ProducesNamespacedResultKind()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "a.mp4"), "x");
        ConfigureFilters(new() { new() { Enabled = true, Keyword = "TF", Folders = { dir.Path }, FilterPattern = "*.mp4" } });

        using var provider = new FileFiltersSearchableItemProvider();
        var item = provider.GetSearchableItems().Single(i => i.Title == "a.mp4");

        Assert.AreEqual("FileFilter_tf", item.ResultKind);
    }

    [TestMethod]
    public void GetSearchableItems_NoKeyword_UsesDefaultResultKind()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "a.mp4"), "x");
        ConfigureFilters(new() { new() { Enabled = true, Folders = { dir.Path }, FilterPattern = "*.mp4" } });

        using var provider = new FileFiltersSearchableItemProvider();
        var item = provider.GetSearchableItems().Single(i => i.Title == "a.mp4");

        Assert.AreEqual("File", item.ResultKind);
    }

    [TestMethod]
    public void GetSearchableItems_NonExistentFolder_IsSkippedWithoutThrowing()
    {
        ConfigureFilters(new() { new() { Enabled = true, Folders = { @"Z:\definitely-not-a-real-swiftlist-dir" }, FilterPattern = "*" } });

        using var provider = new FileFiltersSearchableItemProvider();

        Assert.IsEmpty(provider.GetSearchableItems());
    }

    [TestMethod]
    public void Dispose_DoesNotThrow()
    {
        var provider = new FileFiltersSearchableItemProvider();

        provider.Dispose();
    }
}
