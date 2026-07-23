using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using SwiftList.PluginSdk.Abstractions;

namespace SwiftList.Core.Wire;

public static class SearchResponseBinarySerializer
{
    private const int Magic = 0x53524C53; // SLRS
    private const int Version = 4; // v4: gained Size/Created/Modified/Accessed (SearchResult.Metadata)
    private const byte EndFrame = 0;
    private const byte FileResultFrame = 1;
    private const byte AppResultFrame = 2;
    private const byte HeaderFrame = 255;

    public static async Task WriteHeaderAsync(Stream stream, CancellationToken token = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(13);
        try
        {
            var span = buffer.AsSpan();
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0), Magic);
            span[4] = HeaderFrame;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(5), 4);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(9), Version);

            await stream.WriteAsync(buffer.AsMemory(0, 13), token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }



    public static Task WriteFileResultAsync(Stream stream, SearchResult result, CancellationToken token = default)
        => WriteResultAsync(stream, FileResultFrame, result, token);

    public static async Task WriteEndAsync(Stream stream, CancellationToken token = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(9);
        try
        {
            var span = buffer.AsSpan();
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0), Magic);
            span[4] = EndFrame;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(5), 0);

            await stream.WriteAsync(buffer.AsMemory(0, 9), token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task ReadAsync(Stream stream, Action<SearchResult> onResult, CancellationToken token = default)
    {
        try
        {
            while (true)
            {
                var magic = await ReadInt32Async(stream, token).ConfigureAwait(false);
                if (magic != Magic)
                {
                    Logger.Log($"[Serializer ERROR] Invalid magic: {magic:X}. Expected: {Magic:X}", LogLevel.Error);
                    throw new InvalidDataException($"Invalid search response magic: {magic:X}. Expected: {Magic:X}");
                }

                var frameType = await ReadByteAsync(stream, token).ConfigureAwait(false);
                var length = await ReadInt32Async(stream, token).ConfigureAwait(false);
                if (length < 0 || length > 10 * 1024 * 1024)
                {
                    Logger.Log($"[Serializer ERROR] Invalid length: {length}. Magic={magic:X}, FrameType={frameType}", LogLevel.Error);
                    throw new InvalidDataException($"Invalid search response payload length: {length}");
                }

                var payload = await ReadExactlyAsync(stream, length, token).ConfigureAwait(false);
                if (frameType == EndFrame)
                    return;

                if (frameType == HeaderFrame)
                {
                    if (payload.Length < 4)
                        throw new InvalidDataException("Invalid header payload length.");
                    var version = BinaryPrimitives.ReadInt32LittleEndian(payload);
                    if (version != Version)
                        throw new InvalidDataException($"Unsupported search response binary version: {version}. Expected: {Version}");
                    continue;
                }

                if (frameType == FileResultFrame || frameType == AppResultFrame)
                {
                    var result = ReadResult(payload);
                    onResult(result);
                    continue;
                }

                throw new InvalidDataException($"Unknown search response frame: {frameType}.");
            }
        }
        catch (OperationCanceledException ex)
        {
            Logger.Log($"[Serializer Cancelled] {ex.Message}", LogLevel.Debug);
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Serializer Exception] {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    private static async Task WriteResultAsync(Stream stream, byte frame, SearchResult result, CancellationToken token)
    {
        var name = result.Name ?? string.Empty;
        var path = result.Path ?? string.Empty;
        var drive = result.Drive ?? string.Empty;

        var nameLen = Encoding.UTF8.GetByteCount(name);
        var pathLen = Encoding.UTF8.GetByteCount(path);
        var driveLen = Encoding.UTF8.GetByteCount(drive);

        var maxPayloadSize = nameLen + pathLen + driveLen + 44;
        var totalSize = 9 + maxPayloadSize;

        var buffer = ArrayPool<byte>.Shared.Rent(totalSize);
        try
        {
            var span = buffer.AsSpan();
            var offset = 0;

            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), Magic);
            offset += 4;

            span[offset++] = frame;

            var payloadLengthOffset = offset;
            offset += 4;

            var payloadStart = offset;

            Write7BitEncodedInt(span.Slice(offset), nameLen, out var written);
            offset += written;
            Encoding.UTF8.GetBytes(name, span.Slice(offset));
            offset += nameLen;

            Write7BitEncodedInt(span.Slice(offset), pathLen, out written);
            offset += written;
            Encoding.UTF8.GetBytes(path, span.Slice(offset));
            offset += pathLen;

            span[offset++] = (byte)(result.IsDir ? 1 : 0);

            Write7BitEncodedInt(span.Slice(offset), driveLen, out written);
            offset += written;
            Encoding.UTF8.GetBytes(drive, span.Slice(offset));
            offset += driveLen;

            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset), result.RankSortKey);
            offset += 8;

            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(offset), result.Metadata.Size);
            offset += 8;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset), FileTimeHelper.ToUnixSeconds(result.Metadata.Created.ToUniversalTime()));
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset), FileTimeHelper.ToUnixSeconds(result.Metadata.Modified.ToUniversalTime()));
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset), FileTimeHelper.ToUnixSeconds(result.Metadata.Accessed.ToUniversalTime()));
            offset += 4;

            var payloadLength = offset - payloadStart;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(payloadLengthOffset), payloadLength);

            await stream.WriteAsync(buffer.AsMemory(0, offset), token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void Write7BitEncodedInt(Span<byte> destination, int value, out int bytesWritten)
    {
        bytesWritten = 0;
        var uValue = (uint)value;
        while (uValue >= 0x80)
        {
            destination[bytesWritten++] = (byte)(uValue | 0x80);
            uValue >>= 7;
        }
        destination[bytesWritten++] = (byte)uValue;
    }

    private static int Read7BitEncodedInt(byte[] buffer, ref int offset)
    {
        uint result = 0;
        var shift = 0;
        while (shift < 35)
        {
            var b = buffer[offset++];
            result |= (uint)(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
                return (int)result;
        }
        throw new FormatException("Invalid 7-bit encoded integer.");
    }

    private static string ReadString(byte[] buffer, ref int offset)
    {
        var length = Read7BitEncodedInt(buffer, ref offset);
        if (length == 0) return string.Empty;
        var str = Encoding.UTF8.GetString(buffer, offset, length);
        offset += length;
        return str;
    }

    private static SearchResult ReadResult(byte[] payload)
    {
        var offset = 0;
        var name = ReadString(payload, ref offset);
        var path = ReadString(payload, ref offset);
        var isDir = payload[offset++] != 0;
        var drive = ReadString(payload, ref offset);
        var rankSortKey = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(offset));
        offset += 8;
        var size = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset));
        offset += 8;
        var created = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        var modified = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        var accessed = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
        return new SearchResult
        {
            Name = name,
            Path = path,
            IsDir = isDir,
            Drive = drive,
            RankSortKey = rankSortKey,
            Metadata = new FileMetadata(
                size,
                FileTimeHelper.FromUnixSeconds(created).ToLocalTime(),
                FileTimeHelper.FromUnixSeconds(modified).ToLocalTime(),
                FileTimeHelper.FromUnixSeconds(accessed).ToLocalTime()),
        };
    }

    private static async Task<int> ReadInt32Async(Stream stream, CancellationToken token)
    {
        var bytes = await ReadExactlyAsync(stream, sizeof(int), token).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    private static async Task<byte> ReadByteAsync(Stream stream, CancellationToken token)
    {
        var bytes = await ReadExactlyAsync(stream, 1, token).ConfigureAwait(false);
        return bytes[0];
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken token)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), token).ConfigureAwait(false);
            if (read <= 0)
                throw new EndOfStreamException($"End of stream reached. Read {offset} of {count} bytes.");
            offset += read;
        }
        return buffer;
    }
}
