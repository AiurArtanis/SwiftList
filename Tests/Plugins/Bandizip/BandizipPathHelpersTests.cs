namespace SwiftList.Plugins.Bandizip.Tests;

[TestClass]
public sealed class BandizipPathHelpersTests
{
    [TestMethod]
    public void NormalizeIfExists_PathExists_ReturnsItTrimmed()
    {
        var result = BandizipPathHelpers.NormalizeIfExists(@"C:\Users\hlj49\Desktop\", path => path == @"C:\Users\hlj49\Desktop");

        Assert.AreEqual(@"C:\Users\hlj49\Desktop", result);
    }

    [TestMethod]
    public void NormalizeIfExists_PathDoesNotExist_ReturnsNull()
    {
        // Bandizip's own default extraction folder is one it plans to create -- it commonly doesn't exist
        // yet, and GetCurrentPath's contract is strict: only a real, already-existing folder counts.
        var result = BandizipPathHelpers.NormalizeIfExists(@"C:\Users\hlj49\Desktop\New folder", _ => false);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void NormalizeIfExists_EmptyText_ReturnsNull()
    {
        var result = BandizipPathHelpers.NormalizeIfExists("", _ => true);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void NormalizeIfExists_WhitespaceText_ReturnsNull()
    {
        var result = BandizipPathHelpers.NormalizeIfExists("   ", _ => true);

        Assert.IsNull(result);
    }
}
