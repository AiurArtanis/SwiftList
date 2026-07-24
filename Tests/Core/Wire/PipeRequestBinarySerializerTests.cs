using SwiftList.Core.Wire;

namespace SwiftList.Core.Tests.Wire;

[TestClass]
public sealed class PipeRequestBinarySerializerTests
{
    [TestMethod]
    public async Task WriteStringAsync_ThenReadStringAsync_RoundTrips()
    {
        using var stream = new MemoryStream();
        await PipeRequestBinarySerializer.WriteStringAsync(stream, "ping");
        stream.Position = 0;

        var result = await PipeRequestBinarySerializer.ReadStringAsync(stream);

        Assert.AreEqual("ping", result);
    }

    [TestMethod]
    public async Task WriteMessageAsync_SimpleMessage_RoundTripsId()
    {
        using var stream = new MemoryStream();
        var message = new IpcMessage { Id = IpcMessageId.Stop };
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, message);
        stream.Position = 0;

        var result = await PipeRequestBinarySerializer.ReadMessageAsync(stream);

        Assert.AreEqual(IpcMessageId.Stop, result.Id);
    }

    [TestMethod]
    public async Task WriteMessageAsync_MessageWithHwndAndString_RoundTripsAllFields()
    {
        using var stream = new MemoryStream();
        var message = new IpcMessage
        {
            Id = IpcMessageId.NavigateDialog,
            Hwnd = 0x1234ABCD,
            StringVal1 = @"C:\Users\test"
        };
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, message);
        stream.Position = 0;

        var result = await PipeRequestBinarySerializer.ReadMessageAsync(stream);

        Assert.AreEqual(IpcMessageId.NavigateDialog, result.Id);
        Assert.AreEqual(0x1234ABCD, result.Hwnd);
        Assert.AreEqual(@"C:\Users\test", result.StringVal1);
    }

    [TestMethod]
    public async Task WriteMessageAsync_MouseMessage_RoundTripsCoordinates()
    {
        using var stream = new MemoryStream();
        var message = new IpcMessage { Id = IpcMessageId.MouseClick, MouseX = 100, MouseY = -50 };
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, message);
        stream.Position = 0;

        var result = await PipeRequestBinarySerializer.ReadMessageAsync(stream);

        Assert.AreEqual(100, result.MouseX);
        Assert.AreEqual(-50, result.MouseY);
    }

    [TestMethod]
    public async Task WriteMessageAsync_ExplorerActivated_RoundTripsAllFields()
    {
        using var stream = new MemoryStream();
        var message = new IpcMessage
        {
            Id = IpcMessageId.ExplorerActivated,
            Hwnd = 42,
            StringVal1 = "explorer.exe",
            StringVal2 = @"C:\Windows",
            IsDesktop = true
        };
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, message);
        stream.Position = 0;

        var result = await PipeRequestBinarySerializer.ReadMessageAsync(stream);

        Assert.AreEqual(42, result.Hwnd);
        Assert.AreEqual("explorer.exe", result.StringVal1);
        Assert.AreEqual(@"C:\Windows", result.StringVal2);
        Assert.IsTrue(result.IsDesktop);
    }

    [TestMethod]
    public async Task WriteMessageAsync_MultipleMessages_RoundTripInOrderOnSameStream()
    {
        using var stream = new MemoryStream();
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, new IpcMessage { Id = IpcMessageId.KeyEnter });
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, new IpcMessage { Id = IpcMessageId.KeyEscape });
        stream.Position = 0;

        var first = await PipeRequestBinarySerializer.ReadMessageAsync(stream);
        var second = await PipeRequestBinarySerializer.ReadMessageAsync(stream);

        Assert.AreEqual(IpcMessageId.KeyEnter, first.Id);
        Assert.AreEqual(IpcMessageId.KeyEscape, second.Id);
    }

    [TestMethod]
    public async Task ReadMessageAsync_CorruptedMagicHeader_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => PipeRequestBinarySerializer.ReadMessageAsync(stream));
    }
}
