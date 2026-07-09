using System.Buffers.Binary;

namespace SwiftList.Core;

// The RecentFiles PipeResponse kind's own codec -- kept as its own static class (not a partial split
// of PipeResponseBinarySerializer) to stay under the repo's per-file line limit.
public static class RecentFilesResponseCodec
{
    public static Task WriteRecentFilesAsync(Stream stream, List<SearchResult> recentFiles, CancellationToken token = default)
        => PipeResponseBinarySerializer.WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.RecentFiles, RecentFiles = recentFiles }, token);

    // Name/Path/IsDir/Drive/ModifiedUtc only -- Attributes isn't read by any caller (see
    // SearchResultHelper.CreateUiResult). ModifiedUtc is carried so SearchService.GetRecentFilesAsync can
    // merge this response with the network/WSL result set by actual recency instead of just concatenating.
    internal static int CalculateRecentFilesSize(List<SearchResult> recentFiles)
    {
        var size = 4; // Count
        foreach (var item in recentFiles)
            size += PipeResponseBinarySerializer.GetStringByteCount(item.Name) + 5 + PipeResponseBinarySerializer.GetStringByteCount(item.Path) + 5 + 1 + PipeResponseBinarySerializer.GetStringByteCount(item.Drive) + 5 + 4;
        return size;
    }

    internal static void WriteRecentFiles(Span<byte> span, ref int offset, List<SearchResult> recentFiles)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), recentFiles.Count);
        offset += 4;
        foreach (var item in recentFiles)
        {
            PipeResponseBinarySerializer.WriteString(span, ref offset, item.Name);
            PipeResponseBinarySerializer.WriteString(span, ref offset, item.Path);
            span[offset++] = (byte)(item.IsDir ? 1 : 0);
            PipeResponseBinarySerializer.WriteString(span, ref offset, item.Drive);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset), item.ModifiedUtc);
            offset += 4;
        }
    }

    internal static List<SearchResult> ReadRecentFiles(byte[] payload, ref int offset)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        var recentFiles = new List<SearchResult>(count);
        for (var i = 0; i < count; i++)
        {
            var name = PipeResponseBinarySerializer.ReadString(payload, ref offset);
            var path = PipeResponseBinarySerializer.ReadString(payload, ref offset);
            var isDir = payload[offset++] != 0;
            var drive = PipeResponseBinarySerializer.ReadString(payload, ref offset);
            var modifiedUtc = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
            offset += 4;
            recentFiles.Add(new SearchResult { Name = name, Path = path, IsDir = isDir, Drive = drive, ModifiedUtc = modifiedUtc });
        }
        return recentFiles;
    }
}
