using SwiftList.Plugins.CoreExtensions.Preview.Providers;

namespace SwiftList.Plugins.CoreExtensions.Tests.Preview.Providers;

[TestClass]
public sealed class ImagePreviewProviderTests
{
    private static readonly ImagePreviewProvider Provider = new();

    [TestMethod]
    public void CanPreview_PngFile_ReturnsTrue() => Assert.IsTrue(Provider.CanPreview(@"C:\photo.png", isDir: false));

    [TestMethod]
    public void CanPreview_ExtensionMatchIsCaseInsensitive() => Assert.IsTrue(Provider.CanPreview(@"C:\Photo.PNG", isDir: false));

    [TestMethod]
    public void CanPreview_OtherExtension_ReturnsFalse() => Assert.IsFalse(Provider.CanPreview(@"C:\readme.txt", isDir: false));

    [TestMethod]
    public void CanPreview_Directory_ReturnsFalseEvenWithImageExtension() => Assert.IsFalse(Provider.CanPreview(@"C:\photo.png", isDir: true));
}

[TestClass]
public sealed class TextPreviewProviderTests
{
    private static readonly TextPreviewProvider Provider = new();

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"swiftlist-tests-{Guid.NewGuid():N}");

        public TempFile(int sizeBytes) => File.WriteAllBytes(Path, new byte[sizeBytes]);

        public void Dispose()
        {
            try { File.Delete(Path); } catch { }
        }
    }

    [TestMethod]
    public void CanPreview_Directory_ReturnsFalse() => Assert.IsFalse(Provider.CanPreview(@"C:\somefile.unknownext", isDir: true));

    [TestMethod]
    public void CanPreview_KnownTextExtension_ReturnsTrueEvenIfFileMissing() =>
        // Extension match short-circuits before any FileInfo access, so a nonexistent path is fine.
        Assert.IsTrue(Provider.CanPreview(@"C:\definitely-missing.cs", isDir: false));

    [TestMethod]
    public void CanPreview_UnknownExtension_SmallExistingFile_ReturnsTrue()
    {
        using var file = new TempFile(1024);

        Assert.IsTrue(Provider.CanPreview(file.Path, isDir: false));
    }

    [TestMethod]
    public void CanPreview_UnknownExtension_LargeExistingFile_ReturnsFalse()
    {
        using var file = new TempFile(200_000);

        Assert.IsFalse(Provider.CanPreview(file.Path, isDir: false));
    }

    [TestMethod]
    public void CanPreview_UnknownExtension_MissingFile_ReturnsFalse() =>
        Assert.IsFalse(Provider.CanPreview(@"Z:\definitely-not-real-swiftlist-file.unknownext", isDir: false));
}

[TestClass]
public sealed class DefaultMetadataPreviewProviderTests
{
    private static readonly DefaultMetadataPreviewProvider Provider = new();

    [TestMethod]
    public void CanPreview_AnyFile_ReturnsTrue() => Assert.IsTrue(Provider.CanPreview(@"C:\anything.whatever", isDir: false));

    [TestMethod]
    public void CanPreview_Directory_ReturnsTrue() => Assert.IsTrue(Provider.CanPreview(@"C:\somedir", isDir: true));
}

[TestClass]
public sealed class FolderPreviewProviderTests
{
    private static readonly FolderPreviewProvider Provider = new();

    [TestMethod]
    public void CanPreview_Directory_ReturnsTrue() => Assert.IsTrue(Provider.CanPreview(@"C:\somedir", isDir: true));

    [TestMethod]
    public void CanPreview_File_ReturnsFalse() => Assert.IsFalse(Provider.CanPreview(@"C:\file.txt", isDir: false));
}
