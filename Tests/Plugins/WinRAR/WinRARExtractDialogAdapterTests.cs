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
}
