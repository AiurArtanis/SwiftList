using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using SwiftList.Core.Indexer.Usn;
namespace SwiftList.Core;

public enum PipeResponseKind : byte
{
    Ok = 1,
    Error = 2,
    Status = 3,
    MachineSettings = 4
}
public readonly struct PipeResponse
{
    public PipeResponseKind Kind { get; init; }
    public string Message { get; init; }
    public UsnIndexer.IndexerStatus? Status { get; init; }
    public MachineSettings? MachineSettings { get; init; }
    public bool IsOk => Kind != PipeResponseKind.Error;
}
public static class PipeResponseBinarySerializer
{
    private const int Magic = 0x52504C53; // SLPR
    private const int Version = 3;

    public static Task WriteOkAsync(Stream stream, CancellationToken token = default)
        => WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.Ok }, token);
    public static Task WriteErrorAsync(Stream stream, string message, CancellationToken token = default)
        => WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.Error, Message = message }, token);
    public static Task WriteStatusAsync(Stream stream, UsnIndexer.IndexerStatus status, CancellationToken token = default)
        => WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.Status, Status = status }, token);
    public static Task WriteMachineSettingsAsync(Stream stream, MachineSettings settings, CancellationToken token = default)
        => WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.MachineSettings, MachineSettings = settings }, token);
    public static async Task<PipeResponse> ReadAsync(Stream stream, CancellationToken token = default)
    {
        var magic = await ReadInt32Async(stream, token).ConfigureAwait(false);
        if (magic != Magic)
            throw new InvalidDataException("Invalid pipe response binary header.");

        var version = await ReadInt32Async(stream, token).ConfigureAwait(false);
        if (version != Version)
            throw new InvalidDataException($"Unsupported pipe response binary version: {version}.");

        var length = await ReadInt32Async(stream, token).ConfigureAwait(false);
        if (length < 0 || length > 10 * 1024 * 1024)
            throw new InvalidDataException($"Invalid response payload length: {length}");
        var payload = await ReadExactlyAsync(stream, length, token).ConfigureAwait(false);

        var offset = 0;
        var kind = (PipeResponseKind)payload[offset++];

        return kind switch
        {
            PipeResponseKind.Ok => new PipeResponse { Kind = kind },
            PipeResponseKind.Error => new PipeResponse { Kind = kind, Message = ReadString(payload, ref offset) },
            PipeResponseKind.Status => new PipeResponse { Kind = kind, Status = ReadStatus(payload, ref offset) },
            PipeResponseKind.MachineSettings => new PipeResponse { Kind = kind, MachineSettings = ReadMachineSettings(payload, ref offset) },
            _ => throw new InvalidDataException($"Unknown pipe response kind: {kind}.")
        };
    }

    private static async Task WriteAsync(Stream stream, PipeResponse response, CancellationToken token)
    {
        var payloadSize = 1; // Kind byte
        switch (response.Kind)
        {
            case PipeResponseKind.Error:
                payloadSize += GetStringByteCount(response.Message) + 5;
                break;
            case PipeResponseKind.Status:
                payloadSize += CalculateStatusSize(response.Status ?? new UsnIndexer.IndexerStatus { State = "error" });
                break;
            case PipeResponseKind.MachineSettings:
                payloadSize += CalculateSettingsSize(response.MachineSettings ?? new MachineSettings());
                break;
        }
        var totalSize = 12 + payloadSize; // Magic(4) + Version(4) + Length(4) + Payload
        var buffer = ArrayPool<byte>.Shared.Rent(totalSize);
        try
        {
            var span = buffer.AsSpan();
            var offset = 12;
            span[offset++] = (byte)response.Kind;

            switch (response.Kind)
            {
                case PipeResponseKind.Error:
                    WriteString(span, ref offset, response.Message);
                    break;
                case PipeResponseKind.Status:
                    WriteStatus(span, ref offset, response.Status ?? new UsnIndexer.IndexerStatus { State = "error" });
                    break;
                case PipeResponseKind.MachineSettings:
                    WriteMachineSettings(span, ref offset, response.MachineSettings ?? new MachineSettings());
                    break;
            }

            var actualPayloadSize = offset - 12;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0), Magic);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), Version);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), actualPayloadSize);

            await stream.WriteAsync(buffer.AsMemory(0, offset), token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static int GetStringByteCount(string? str) => Encoding.UTF8.GetByteCount(str ?? string.Empty);

    private static int CalculateStatusSize(UsnIndexer.IndexerStatus status)
    {
        var size = GetStringByteCount(status.State) + 5;
        size += 21; // Progress(4) + TotalFiles(4) + TotalDirs(4) + ElapsedTime(8) + IsMaintenanceBusy(1)
        size += 4;  // ActiveDrives count
        foreach (var drive in status.ActiveDrives)
            size += GetStringByteCount(drive) + 5;

        size += 4;  // Drives count
        foreach (var drive in status.Drives)
        {
            size += GetStringByteCount(drive.Drive) + 5;
            size += 1; // Enabled
            size += GetStringByteCount(drive.Kind) + 5;
            size += GetStringByteCount(drive.State) + 5;
            size += 8; // Files(4) + Dirs(4)
            size += GetStringByteCount(drive.CachePath) + 5;
        }
        return size;
    }
    private static int CalculateSettingsSize(MachineSettings settings)
    {
        var size = 4; // Count
        foreach (var drive in settings.EnabledLocalDrives)
            size += GetStringByteCount(drive) + 5;
        return size;
    }
    private static void WriteString(Span<byte> buffer, ref int offset, string? str)
    {
        var s = str ?? string.Empty;
        var len = Encoding.UTF8.GetByteCount(s);
        Write7BitEncodedInt(buffer, ref offset, len);
        Encoding.UTF8.GetBytes(s, buffer.Slice(offset));
        offset += len;
    }
    private static void WriteStatus(Span<byte> span, ref int offset, UsnIndexer.IndexerStatus status)
    {
        WriteString(span, ref offset, status.State);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), status.Progress);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), status.TotalFiles);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), status.TotalDirs);
        offset += 4;
        BinaryPrimitives.WriteDoubleLittleEndian(span.Slice(offset), status.ElapsedTime);
        offset += 8;
        span[offset++] = (byte)(status.IsMaintenanceBusy ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), status.ActiveDrives.Count);
        offset += 4;
        foreach (var drive in status.ActiveDrives)
            WriteString(span, ref offset, drive);

        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), status.Drives.Count);
        offset += 4;
        foreach (var drive in status.Drives)
        {
            WriteString(span, ref offset, drive.Drive);
            span[offset++] = (byte)(drive.Enabled ? 1 : 0);
            WriteString(span, ref offset, drive.Kind);
            WriteString(span, ref offset, drive.State);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), drive.Files);
            offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), drive.Dirs);
            offset += 4;
            WriteString(span, ref offset, drive.CachePath);
        }
    }
    private static void WriteMachineSettings(Span<byte> span, ref int offset, MachineSettings settings)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), settings.EnabledLocalDrives.Count);
        offset += 4;
        foreach (var drive in settings.EnabledLocalDrives)
            WriteString(span, ref offset, drive);
    }
    private static UsnIndexer.IndexerStatus ReadStatus(byte[] payload, ref int offset)
    {
        var status = new UsnIndexer.IndexerStatus
        {
            State = ReadString(payload, ref offset),
            Progress = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset)),
            TotalFiles = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4)),
            TotalDirs = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 8)),
            ElapsedTime = BinaryPrimitives.ReadDoubleLittleEndian(payload.AsSpan(offset + 12)),
            IsMaintenanceBusy = payload[offset + 20] != 0
        };
        offset += 21;

        var activeCount = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        for (var i = 0; i < activeCount; i++)
            status.ActiveDrives.Add(ReadString(payload, ref offset));

        var driveCount = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        for (var i = 0; i < driveCount; i++)
        {
            var drive = ReadString(payload, ref offset);
            var enabled = payload[offset++] != 0;
            var kind = ReadString(payload, ref offset);
            var state = ReadString(payload, ref offset);
            var files = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
            offset += 4;
            var dirs = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
            offset += 4;
            var cachePath = ReadString(payload, ref offset);

            status.Drives.Add(new UsnIndexer.DriveIndexStatus
            {
                Drive = drive,
                Enabled = enabled,
                Kind = kind,
                State = state,
                Files = files,
                Dirs = dirs,
                CachePath = cachePath
            });
        }
        return status;
    }

    private static MachineSettings ReadMachineSettings(byte[] payload, ref int offset)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        var settings = new MachineSettings();
        for (var i = 0; i < count; i++)
            settings.EnabledLocalDrives.Add(ReadString(payload, ref offset));
        return settings;
    }

    private static void Write7BitEncodedInt(Span<byte> destination, ref int offset, int value)
    {
        var uValue = (uint)value;
        while (uValue >= 0x80)
        {
            destination[offset++] = (byte)(uValue | 0x80);
            uValue >>= 7;
        }
        destination[offset++] = (byte)uValue;
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

    private static async Task<int> ReadInt32Async(Stream stream, CancellationToken token)
    {
        var bytes = await ReadExactlyAsync(stream, sizeof(int), token).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
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
