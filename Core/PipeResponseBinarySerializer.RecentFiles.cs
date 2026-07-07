using System.Buffers.Binary;

namespace SwiftList.Core;

// The RecentFiles response kind's own codec, split out of PipeResponseBinarySerializer.cs to stay
// under the repo's per-file line limit -- see the class-level comment there.
public static partial class PipeResponseBinarySerializer
{
    public static Task WriteRecentFilesAsync(Stream stream, List<SearchResult> recentFiles, CancellationToken token = default)
        => WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.RecentFiles, RecentFiles = recentFiles }, token);

    // Name/Path/IsDir/Drive/CreatedUtc only -- Attributes isn't read by any caller (see
    // SearchResultHelper.CreateUiResult). CreatedUtc is carried so SearchService.GetRecentFilesAsync can
    // merge this response with the network/WSL result set by actual recency instead of just concatenating.
    private static int CalculateRecentFilesSize(List<SearchResult> recentFiles)
    {
        var size = 4; // Count
        foreach (var item in recentFiles)
            size += GetStringByteCount(item.Name) + 5 + GetStringByteCount(item.Path) + 5 + 1 + GetStringByteCount(item.Drive) + 5 + 4;
        return size;
    }

    private static void WriteRecentFiles(Span<byte> span, ref int offset, List<SearchResult> recentFiles)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), recentFiles.Count);
        offset += 4;
        foreach (var item in recentFiles)
        {
            WriteString(span, ref offset, item.Name);
            WriteString(span, ref offset, item.Path);
            span[offset++] = (byte)(item.IsDir ? 1 : 0);
            WriteString(span, ref offset, item.Drive);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset), item.CreatedUtc);
            offset += 4;
        }
    }

    private static List<SearchResult> ReadRecentFiles(byte[] payload, ref int offset)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        var recentFiles = new List<SearchResult>(count);
        for (var i = 0; i < count; i++)
        {
            var name = ReadString(payload, ref offset);
            var path = ReadString(payload, ref offset);
            var isDir = payload[offset++] != 0;
            var drive = ReadString(payload, ref offset);
            var createdUtc = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
            offset += 4;
            recentFiles.Add(new SearchResult { Name = name, Path = path, IsDir = isDir, Drive = drive, CreatedUtc = createdUtc });
        }
        return recentFiles;
    }
}
