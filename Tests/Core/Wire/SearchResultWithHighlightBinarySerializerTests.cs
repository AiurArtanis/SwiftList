using SwiftList.Core.Wire;
using SwiftList.PluginSdk.Abstractions;

namespace SwiftList.Core.Tests.Wire;

[TestClass]
public sealed class SearchResultWithHighlightBinarySerializerTests
{
    private static SearchResult MakeResult(string name, string path, FileAttributes attributes = FileAttributes.Normal) => new()
    {
        Name = name,
        Path = path,
        Drive = "C",
        Attributes = attributes,
        Metadata = new FileMetadata(512, DateTime.UtcNow.ToLocalTime(), DateTime.UtcNow.ToLocalTime(), DateTime.UtcNow.ToLocalTime())
    };

    private static async Task<(SearchResult Result, int[] Ranges)> RoundTripSingleAsync(SearchResult result, IReadOnlyList<int> ranges)
    {
        using var stream = new MemoryStream();
        await SearchResultWithHighlightBinarySerializer.WriteHeaderAsync(stream);
        await SearchResultWithHighlightBinarySerializer.WriteFileResultAsync(stream, result, ranges);
        await SearchResultWithHighlightBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        SearchResult? captured = null;
        int[]? capturedRanges = null;
        await SearchResultWithHighlightBinarySerializer.ReadAsync(stream, (r, ranges) =>
        {
            captured = r;
            capturedRanges = ranges;
        });

        return (captured!, capturedRanges!);
    }

    [TestMethod]
    public async Task RoundTrip_WithHighlightRanges_PreservesResultAndRanges()
    {
        var (result, ranges) = await RoundTripSingleAsync(MakeResult("readme.txt", @"c:\readme.txt"), new[] { 0, 4, 7, 3 });

        Assert.AreEqual("readme.txt", result.Name);
        Assert.AreEqual(@"c:\readme.txt", result.Path);
        CollectionAssert.AreEqual(new[] { 0, 4, 7, 3 }, ranges);
    }

    [TestMethod]
    public async Task RoundTrip_NoHighlightRanges_ReturnsEmptyRangesArray()
    {
        var (_, ranges) = await RoundTripSingleAsync(MakeResult("readme.txt", @"c:\readme.txt"), Array.Empty<int>());

        Assert.IsEmpty(ranges);
    }

    [TestMethod]
    public async Task RoundTrip_UnicodeName_PreservesExactText()
    {
        var (result, _) = await RoundTripSingleAsync(MakeResult("文件搜索.txt", @"c:\文件搜索.txt"), Array.Empty<int>());

        Assert.AreEqual("文件搜索.txt", result.Name);
    }

    [TestMethod]
    public async Task RoundTrip_HiddenSystemAttributes_RoundTrips()
    {
        var (result, _) = await RoundTripSingleAsync(
            MakeResult("$MFT", @"c:\$MFT", FileAttributes.Hidden | FileAttributes.System), Array.Empty<int>());

        Assert.AreEqual(FileAttributes.Hidden | FileAttributes.System, result.Attributes);
    }

    [TestMethod]
    public async Task ReadAsync_MismatchedMagic_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream(new byte[] { 9, 9, 9, 9, 0, 0, 0, 0, 0 });

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => SearchResultWithHighlightBinarySerializer.ReadAsync(stream, (_, _) => { }));
    }

    [TestMethod]
    public void FlattenMask_NullMask_ReturnsEmptyArray()
    {
        var ranges = SearchResultWithHighlightBinarySerializer.FlattenMask(null);

        Assert.IsEmpty(ranges);
    }

    [TestMethod]
    public void FlattenMask_AllFalse_ReturnsEmptyArray()
    {
        var ranges = SearchResultWithHighlightBinarySerializer.FlattenMask(new[] { false, false, false });

        Assert.IsEmpty(ranges);
    }

    [TestMethod]
    public void FlattenMask_SingleContiguousRun_ReturnsOneStartLengthPair()
    {
        // Indices 1,2,3 are true -> one run starting at 1, length 3.
        var ranges = SearchResultWithHighlightBinarySerializer.FlattenMask(new[] { false, true, true, true, false });

        CollectionAssert.AreEqual(new[] { 1, 3 }, ranges);
    }

    [TestMethod]
    public void FlattenMask_MultipleDisjointRuns_ReturnsPairPerRun()
    {
        // true at [0], false at [1], true at [2,3] -> two runs: (0,1) and (2,2).
        var ranges = SearchResultWithHighlightBinarySerializer.FlattenMask(new[] { true, false, true, true });

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 2 }, ranges);
    }

    [TestMethod]
    public void FlattenMask_EntireMaskTrue_ReturnsOneRunCoveringWholeMask()
    {
        var ranges = SearchResultWithHighlightBinarySerializer.FlattenMask(new[] { true, true, true });

        CollectionAssert.AreEqual(new[] { 0, 3 }, ranges);
    }
}
