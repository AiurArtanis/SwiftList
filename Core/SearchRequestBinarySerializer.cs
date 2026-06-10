using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace SwiftList.Core
{
    public static class SearchRequestBinarySerializer
    {
        private const int Magic = 0x51504C53; // SLPQ

        private const int VersionSearchRequest = 3;

        public static async Task WriteSearchRequestAsync(Stream stream, SearchRequestMessage msg, CancellationToken token = default)
        {
            using var ms = new MemoryStream();
            using (var msWriter = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                WritePayload(msWriter, msg);
            }

            await WriteFrameAsync(stream, ms.ToArray(), token).ConfigureAwait(false);
        }

        public static async Task<SearchRequestMessage> ReadSearchRequestAsync(Stream stream, CancellationToken token = default)
        {
            int magic = await ReadInt32Async(stream, token).ConfigureAwait(false);
            if (magic != Magic)
                throw new InvalidDataException("Invalid pipe request binary header.");
            int version = await ReadInt32Async(stream, token).ConfigureAwait(false);
            if (version != VersionSearchRequest)
                throw new InvalidDataException($"Unsupported pipe search request version: {version}.");
            int length = await ReadInt32Async(stream, token).ConfigureAwait(false);
            if (length < 0 || length > 10 * 1024 * 1024)
                throw new InvalidDataException($"Invalid search request payload length: {length}");
            byte[] payload = await ReadExactlyAsync(stream, length, token).ConfigureAwait(false);
            using var ms = new MemoryStream(payload);
            using var msReader = new BinaryReader(ms, Encoding.UTF8);

            return ReadPayload(msReader);
        }

        private static void WritePayload(BinaryWriter writer, SearchRequestMessage msg)
        {
            writer.Write((byte)msg.Id);
            switch (msg.Id)
            {
                case SearchRequestId.Status:
                case SearchRequestId.Rebuild:
                case SearchRequestId.GetMachineSettings:
                    break;

                case SearchRequestId.SetMachineSettings:
                    WriteMachineSettings(writer, msg.MachineSettings ?? new MachineSettings());
                    break;

                case SearchRequestId.Search:
                    writer.Write(msg.Limit);
                    writer.Write(msg.AppLimit);
                    writer.Write(msg.Query ?? string.Empty);
                    break;

                case SearchRequestId.SearchDir:
                    writer.Write(msg.Limit);
                    writer.Write(msg.AppLimit);
                    writer.Write(msg.DirectoryFilter ?? string.Empty);
                    writer.Write(msg.Query ?? string.Empty);
                    break;
            }
        }

        private static SearchRequestMessage ReadPayload(BinaryReader reader)
        {
            var msg = new SearchRequestMessage { Id = (SearchRequestId)reader.ReadByte() };
            switch (msg.Id)
            {
                case SearchRequestId.Status:
                case SearchRequestId.Rebuild:
                case SearchRequestId.GetMachineSettings:
                    break;

                case SearchRequestId.SetMachineSettings:
                    msg.MachineSettings = ReadMachineSettings(reader);
                    break;

                case SearchRequestId.Search:
                    msg.Limit = reader.ReadInt32();
                    msg.AppLimit = reader.ReadInt32();
                    msg.Query = reader.ReadString();
                    break;

                case SearchRequestId.SearchDir:
                    msg.Limit = reader.ReadInt32();
                    msg.AppLimit = reader.ReadInt32();
                    msg.DirectoryFilter = reader.ReadString();
                    msg.Query = reader.ReadString();
                    break;
            }

            return msg;
        }

        private static void WriteMachineSettings(BinaryWriter writer, MachineSettings settings)
        {
            writer.Write(settings.EnabledLocalDrives.Count);
            foreach (var drive in settings.EnabledLocalDrives)
                writer.Write(drive ?? string.Empty);
        }

        private static MachineSettings ReadMachineSettings(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var settings = new MachineSettings();
            for (int i = 0; i < count; i++)
                settings.EnabledLocalDrives.Add(reader.ReadString());
            return settings;
        }

        private static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken token)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(VersionSearchRequest);
                writer.Write(payload.Length);
                writer.Write(payload);
            }

            await stream.WriteAsync(ms.ToArray(), token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }

        private static async Task<int> ReadInt32Async(Stream stream, CancellationToken token)
        {
            byte[] bytes = await ReadExactlyAsync(stream, sizeof(int), token).ConfigureAwait(false);
            return BitConverter.ToInt32(bytes, 0);
        }

        private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken token)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), token).ConfigureAwait(false);
                if (read <= 0)
                    throw new EndOfStreamException($"End of stream reached. Read {offset} of {count} bytes.");
                offset += read;
            }

            return buffer;
        }
    }
}
