using System.Text;
namespace SwiftList.Core;

public static class SearchResponseBinarySerializer
{
    private const int Magic = 0x53524C53; // SLRS

    private const int Version = 3;
    private const byte EndFrame = 0;
    private const byte FileResultFrame = 1;
    private const byte AppResultFrame = 2;
    private const byte HeaderFrame = 255;

    public static async Task WriteHeaderAsync(Stream stream, CancellationToken token = default)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        writer.Write(Version);
        writer.Flush();
        await WriteFrameAsync(stream, HeaderFrame, ms.ToArray(), token).ConfigureAwait(false);
    }

    public static Task WriteAppResultAsync(Stream stream, SearchResult result, CancellationToken token = default)

        => WriteResultAsync(stream, AppResultFrame, result, token);

    public static Task WriteFileResultAsync(Stream stream, SearchResult result, CancellationToken token = default)

        => WriteResultAsync(stream, FileResultFrame, result, token);

    public static Task WriteEndAsync(Stream stream, CancellationToken token = default)

        => WriteFrameAsync(stream, EndFrame, Array.Empty<byte>(), token);

    public static async Task ReadAsync(Stream stream, Action<SearchResult, bool> onResult, CancellationToken token = default)
    {
        while (true)
        {
            var magic = await ReadInt32Async(stream, token).ConfigureAwait(false);
            if (magic != Magic)
                throw new InvalidDataException($"Invalid search response magic: {magic:X}. Expected: {Magic:X}");
            var frameType = await ReadByteAsync(stream, token).ConfigureAwait(false);
            var length = await ReadInt32Async(stream, token).ConfigureAwait(false);
            if (length < 0 || length > 10 * 1024 * 1024)
                throw new InvalidDataException($"Invalid search response payload length: {length}");
            var payload = await ReadExactlyAsync(stream, length, token).ConfigureAwait(false);
            if (frameType == EndFrame)
                return;
            if (frameType == HeaderFrame)
            {
                using var ms = new MemoryStream(payload);
                using var msReader = new BinaryReader(ms, Encoding.UTF8);

                var version = msReader.ReadInt32();
                if (version != Version)
                    throw new InvalidDataException($"Unsupported search response binary version: {version}. Expected: {Version}");
                continue;
            }

            if (frameType == FileResultFrame || frameType == AppResultFrame)
            {
                using var ms = new MemoryStream(payload);
                using var msReader = new BinaryReader(ms, Encoding.UTF8);

                var result = ReadResult(msReader);
                onResult(result, frameType == AppResultFrame);
                continue;
            }

            throw new InvalidDataException($"Unknown search response frame: {frameType}.");
        }
    }

    private static byte[] ReadExactly(BinaryReader reader, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = reader.Read(buffer, offset, count - offset);
            if (read <= 0)
                throw new EndOfStreamException($"End of stream reached. Read {offset} of {count} bytes.");
            offset += read;
        }

        return buffer;
    }

    private static void WriteResults(BinaryWriter writer, List<SearchResult> results, byte frame)
    {
        foreach (var result in results)
            WriteResult(writer, frame, result);
    }

    private static void WriteHeader(BinaryWriter writer)
    {
        using var ms = new MemoryStream();
        using var msWriter = new BinaryWriter(ms, Encoding.UTF8);

        msWriter.Write(Version);
        msWriter.Flush();
        var payload = ms.ToArray();
        writer.Write(Magic);
        writer.Write(HeaderFrame);
        writer.Write(payload.Length);
        writer.Write(payload);
        writer.Flush();
    }

    private static void WriteResult(BinaryWriter writer, byte frame, SearchResult result)
    {
        using var ms = new MemoryStream();
        using var msWriter = new BinaryWriter(ms, Encoding.UTF8);

        msWriter.Write(result.Name ?? string.Empty);
        msWriter.Write(result.Path ?? string.Empty);
        msWriter.Write(result.IsDir);
        msWriter.Write(result.Drive ?? string.Empty);
        msWriter.Write(result.RankSortKey);
        msWriter.Flush();
        var payload = ms.ToArray();
        writer.Write(Magic);
        writer.Write(frame);
        writer.Write(payload.Length);
        writer.Write(payload);
        writer.Flush();
    }

    private static SearchResult ReadResult(BinaryReader reader) => new SearchResult
    {
        Name = reader.ReadString(),
        Path = reader.ReadString(),
        IsDir = reader.ReadBoolean(),
        Drive = reader.ReadString(),
        RankSortKey = reader.ReadUInt64()

    };

    private static Task WriteResultAsync(Stream stream, byte frame, SearchResult result, CancellationToken token)
    {
        using var ms = new MemoryStream();
        using var msWriter = new BinaryWriter(ms, Encoding.UTF8);

        msWriter.Write(result.Name ?? string.Empty);
        msWriter.Write(result.Path ?? string.Empty);
        msWriter.Write(result.IsDir);
        msWriter.Write(result.Drive ?? string.Empty);
        msWriter.Write(result.RankSortKey);
        msWriter.Flush();
        return WriteFrameAsync(stream, frame, ms.ToArray(), token);
    }

    private static async Task WriteFrameAsync(Stream stream, byte frame, byte[] payload, CancellationToken token)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(frame);
            writer.Write(payload.Length);
            writer.Write(payload);
        }

        await stream.WriteAsync(ms.ToArray(), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task<int> ReadInt32Async(Stream stream, CancellationToken token)
    {
        var bytes = await ReadExactlyAsync(stream, sizeof(int), token).ConfigureAwait(false);
        return BitConverter.ToInt32(bytes, 0);
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
