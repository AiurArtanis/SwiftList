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

    private const int Version = 2;

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
        using var ms = new MemoryStream(payload);
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        var kind = (PipeResponseKind)reader.ReadByte();
        return kind switch
        {
            PipeResponseKind.Ok => new PipeResponse { Kind = kind },

            PipeResponseKind.Error => new PipeResponse { Kind = kind, Message = reader.ReadString() },

            PipeResponseKind.Status => new PipeResponse { Kind = kind, Status = ReadStatus(reader) },

            PipeResponseKind.MachineSettings => new PipeResponse { Kind = kind, MachineSettings = ReadMachineSettings(reader) },

            _ => throw new InvalidDataException($"Unknown pipe response kind: {kind}.")

        };
    }

    private static async Task WriteAsync(Stream stream, PipeResponse response, CancellationToken token)
    {
        using var payloadStream = new MemoryStream();
        using (var payloadWriter = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
        {
            payloadWriter.Write((byte)response.Kind);
            switch (response.Kind)
            {
                case PipeResponseKind.Ok:
                    break;

                case PipeResponseKind.Error:
                    payloadWriter.Write(response.Message ?? string.Empty);
                    break;

                case PipeResponseKind.Status:
                    WriteStatus(payloadWriter, response.Status ?? new UsnIndexer.IndexerStatus { State = "error" });
                    break;

                case PipeResponseKind.MachineSettings:
                    WriteMachineSettings(payloadWriter, response.MachineSettings ?? new MachineSettings());
                    break;
            }
        }

        var payload = payloadStream.ToArray();
        using var frameStream = new MemoryStream();
        using (var writer = new BinaryWriter(frameStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(payload.Length);
            writer.Write(payload);
        }

        await stream.WriteAsync(frameStream.ToArray(), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static void WriteStatus(BinaryWriter writer, UsnIndexer.IndexerStatus status)
    {
        writer.Write(status.State ?? string.Empty);
        writer.Write(status.Progress);
        writer.Write(status.TotalFiles);
        writer.Write(status.TotalDirs);
        writer.Write(status.ElapsedTime);
        writer.Write(status.ActiveDrives.Count);
        foreach (var drive in status.ActiveDrives)
            writer.Write(drive ?? string.Empty);
        writer.Write(status.Drives.Count);
        foreach (var drive in status.Drives)
        {
            writer.Write(drive.Drive ?? string.Empty);
            writer.Write(drive.Enabled);
            writer.Write(drive.Kind ?? string.Empty);
            writer.Write(drive.State ?? string.Empty);
            writer.Write(drive.Files);
            writer.Write(drive.Dirs);
            writer.Write(drive.CachePath ?? string.Empty);
        }
    }

    private static UsnIndexer.IndexerStatus ReadStatus(BinaryReader reader)
    {
        var status = new UsnIndexer.IndexerStatus
        {
            State = reader.ReadString(),
            Progress = reader.ReadInt32(),
            TotalFiles = reader.ReadInt32(),
            TotalDirs = reader.ReadInt32(),
            ElapsedTime = reader.ReadDouble()

        };
        var activeCount = reader.ReadInt32();
        for (var i = 0; i < activeCount; i++)
            status.ActiveDrives.Add(reader.ReadString());
        var driveCount = reader.ReadInt32();
        for (var i = 0; i < driveCount; i++)
        {
            status.Drives.Add(new UsnIndexer.DriveIndexStatus
            {
                Drive = reader.ReadString(),
                Enabled = reader.ReadBoolean(),
                Kind = reader.ReadString(),
                State = reader.ReadString(),
                Files = reader.ReadInt32(),
                Dirs = reader.ReadInt32(),
                CachePath = reader.ReadString()

            });
        }

        return status;
    }

    private static void WriteMachineSettings(BinaryWriter writer, MachineSettings settings)
    {
        writer.Write(settings.EnabledLocalDrives.Count);
        foreach (var drive in settings.EnabledLocalDrives)
            writer.Write(drive ?? string.Empty);
    }

    private static MachineSettings ReadMachineSettings(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var settings = new MachineSettings();
        for (var i = 0; i < count; i++)
            settings.EnabledLocalDrives.Add(reader.ReadString());
        return settings;
    }

    private static async Task<int> ReadInt32Async(Stream stream, CancellationToken token)
    {
        var bytes = await ReadExactlyAsync(stream, sizeof(int), token).ConfigureAwait(false);
        return BitConverter.ToInt32(bytes, 0);
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
