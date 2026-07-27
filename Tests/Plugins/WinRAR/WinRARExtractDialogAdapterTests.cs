namespace SwiftList.Plugins.WinRAR.Tests;

[TestClass]
public sealed class WinRARExtractDialogAdapterTests
{
    [TestMethod]
    public void NormalizeIfExists_PathExists_ReturnsItTrimmed()
    {
        var result = WinRARExtractDialogAdapter.NormalizeIfExists(@"C:\Users\hlj49\Desktop\", path => path == @"C:\Users\hlj49\Desktop");

        Assert.AreEqual(@"C:\Users\hlj49\Desktop", result);
    }

    [TestMethod]
    public void NormalizeIfExists_PathDoesNotExist_ReturnsNull()
    {
        // WinRAR's own default extraction folder is one it plans to create -- it commonly doesn't exist
        // yet, and GetCurrentPath's contract (unlike IInlineSearchAdapter.GetSearchScope elsewhere in this
        // repo) is strict: only a real, already-existing folder counts.
        var result = WinRARExtractDialogAdapter.NormalizeIfExists(@"C:\Users\hlj49\Desktop\New ZIP Archive", _ => false);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void NormalizeIfExists_EmptyText_ReturnsNull()
    {
        var result = WinRARExtractDialogAdapter.NormalizeIfExists("", _ => true);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void NormalizeIfExists_WhitespaceText_ReturnsNull()
    {
        var result = WinRARExtractDialogAdapter.NormalizeIfExists("   ", _ => true);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ResolveTargetFolder_ExistingFile_ReturnsContainingFolder()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var file = Path.Combine(dir.FullName, "notes.txt");
            File.WriteAllText(file, string.Empty);

            var result = WinRARExtractDialogAdapter.ResolveTargetFolder(file);

            Assert.AreEqual(dir.FullName, result);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ResolveTargetFolder_ExistingFolder_ReturnsItUnchanged()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var result = WinRARExtractDialogAdapter.ResolveTargetFolder(dir.FullName);

            Assert.AreEqual(dir.FullName, result);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ResolveTargetFolder_NonExistentPath_ReturnsItUnchanged()
    {
        // WinRAR creates the destination folder on extract if it doesn't exist yet -- a not-yet-created
        // folder must stay as-is, not get walked up a level as if it were a file's parent.
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var result = WinRARExtractDialogAdapter.ResolveTargetFolder(path);

        Assert.AreEqual(path, result);
    }
}
