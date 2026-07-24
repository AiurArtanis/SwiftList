using SwiftList.Core.Wire;
using SwiftList.PluginSdk.Abstractions;

namespace SwiftList.Core.Tests.Wire;

[TestClass]
public sealed class SearchResponseBinarySerializerTests
{
    private static SearchResult MakeResult(string name, string path, bool isDir = false, string drive = "C") => new()
    {
        Name = name,
        Path = path,
        IsDir = isDir,
        Drive = drive,
        Metadata = new FileMetadata(
            1024,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToLocalTime(),
            new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc).ToLocalTime(),
            new DateTime(2024, 6, 20, 8, 0, 0, DateTimeKind.Utc).ToLocalTime())
    };

    [TestMethod]
    public async Task ReadAsync_HeaderThenEnd_InvokesCallbackZeroTimes()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public async Task ReadAsync_SingleFileResult_RoundTripsNamePathAndFlags()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("readme.txt", @"c:\readme.txt"));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        Assert.HasCount(1, results);
        Assert.AreEqual("readme.txt", results[0].Name);
        Assert.AreEqual(@"c:\readme.txt", results[0].Path);
        Assert.IsFalse(results[0].IsDir);
        Assert.AreEqual("C", results[0].Drive);
    }

    [TestMethod]
    public async Task ReadAsync_SingleResult_RoundTripsMetadataFields()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("readme.txt", @"c:\readme.txt"));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        var metadata = results[0].Metadata;
        Assert.AreEqual(1024, metadata.Size);
        Assert.AreEqual(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), metadata.Created.ToUniversalTime());
        Assert.AreEqual(new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc), metadata.Modified.ToUniversalTime());
        Assert.AreEqual(new DateTime(2024, 6, 20, 8, 0, 0, DateTimeKind.Utc), metadata.Accessed.ToUniversalTime());
    }

    [TestMethod]
    public async Task ReadAsync_DirectoryResult_PreservesIsDirFlag()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("Projects", @"c:\Projects", isDir: true));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        Assert.IsTrue(results[0].IsDir);
    }

    [TestMethod]
    public async Task ReadAsync_MultipleResults_PreservesWriteOrder()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("a.txt", @"c:\a.txt"));
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("b.txt", @"c:\b.txt"));
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("c.txt", @"c:\c.txt"));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt", "c.txt" }, results.ConvertAll(r => r.Name));
    }

    [TestMethod]
    public async Task ReadAsync_UnicodeNameAndPath_RoundTripsExactly()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("文件搜索.txt", @"c:\文件搜索.txt"));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        Assert.AreEqual("文件搜索.txt", results[0].Name);
        Assert.AreEqual(@"c:\文件搜索.txt", results[0].Path);
    }

    [TestMethod]
    public async Task ReadAsync_HeaderWithWrongVersion_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream();
        // Write a valid file-result frame first (any frame with this serializer's own magic), which
        // ReadAsync will misinterpret as a header since we craft the header bytes manually below with
        // a bad version -- simplest way to hit the version check without touching internals.
        var badHeader = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(badHeader, 0x53524C53); // magic
        badHeader[4] = 255; // HeaderFrame
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(badHeader.AsSpan(5), 4); // length
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(badHeader.AsSpan(9), 999); // bad version
        await stream.WriteAsync(badHeader);
        stream.Position = 0;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => SearchResponseBinarySerializer.ReadAsync(stream, _ => { }));
    }

    [TestMethod]
    public async Task ReadAsync_CorruptedMagic_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => SearchResponseBinarySerializer.ReadAsync(stream, _ => { }));
    }
}
