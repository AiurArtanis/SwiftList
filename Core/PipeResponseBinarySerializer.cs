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
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(text ?? string.Empty);
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

            return reader.ReadString();
        }
    }
}
