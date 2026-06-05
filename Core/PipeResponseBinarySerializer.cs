using System.IO;
using System.Text;

namespace SwiftList.Core
{
    public static class PipeResponseBinarySerializer
    {
        private const int Magic = 0x52504C53; // SLPR
        private const int Version = 1;

        public static void WriteText(Stream stream, string text)
        {
            using var ms = new MemoryStream();
            using (var msWriter = new BinaryWriter(ms, Encoding.UTF8))
            {
                msWriter.Write(text ?? string.Empty);
            }
            byte[] payload = ms.ToArray();

            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(payload.Length);
            writer.Write(payload);
            writer.Flush();
        }

        public static string ReadText(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            int magic = reader.ReadInt32();
            if (magic != Magic)
                throw new InvalidDataException("Invalid pipe response binary header.");

            int version = reader.ReadInt32();
            if (version != Version)
                throw new InvalidDataException($"Unsupported pipe response binary version: {version}.");

            int length = reader.ReadInt32();
            if (length < 0 || length > 10 * 1024 * 1024) // 10MB limit
                throw new InvalidDataException($"Invalid response payload length: {length}");

            byte[] payload = ReadExactly(reader, length);
            using var ms = new MemoryStream(payload);
            using var msReader = new BinaryReader(ms, Encoding.UTF8);
            return msReader.ReadString();
        }

        private static byte[] ReadExactly(BinaryReader reader, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = reader.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException($"End of stream reached. Read {offset} of {count} bytes.");
                offset += read;
            }
            return buffer;
        }
    }
}
