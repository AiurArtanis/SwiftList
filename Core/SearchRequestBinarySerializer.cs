using System;
using System.IO;
using System.Text;

namespace SwiftList.Core
{
    public static class SearchRequestBinarySerializer
    {
        private const int Magic = 0x51504C53; // SLPQ
        private const int VersionSearchRequest = 3;

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

        public static void WriteSearchRequest(Stream stream, SearchRequestMessage msg)
        {
            using var ms = new MemoryStream();
            using (var msWriter = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                msWriter.Write((byte)msg.Id);

                switch (msg.Id)
                {
                    case SearchRequestId.Status:
                    case SearchRequestId.Rebuild:
                    case SearchRequestId.GetMachineSettings:
                        break;
                    case SearchRequestId.SetMachineSettings:
                        msWriter.Write(msg.JsonSettings ?? string.Empty);
                        break;
                    case SearchRequestId.Search:
                        msWriter.Write(msg.Limit);
                        msWriter.Write(msg.AppLimit);
                        msWriter.Write(msg.Query ?? string.Empty);
                        break;
                    case SearchRequestId.SearchDir:
                        msWriter.Write(msg.Limit);
                        msWriter.Write(msg.AppLimit);
                        msWriter.Write(msg.DirectoryFilter ?? string.Empty);
                        msWriter.Write(msg.Query ?? string.Empty);
                        break;
                }
            }
            byte[] payload = ms.ToArray();

            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(VersionSearchRequest);
            writer.Write(payload.Length);
            writer.Write(payload);
            writer.Flush();
        }

        public static SearchRequestMessage ReadSearchRequest(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            int magic = reader.ReadInt32();
            if (magic != Magic)
                throw new InvalidDataException("Invalid pipe request binary header.");

            int version = reader.ReadInt32();
            if (version != VersionSearchRequest)
                throw new InvalidDataException($"Unsupported pipe search request version: {version}.");

            int length = reader.ReadInt32();
            if (length < 0 || length > 10 * 1024 * 1024)
                throw new InvalidDataException($"Invalid search request payload length: {length}");

            byte[] payload = ReadExactly(reader, length);
            using var ms = new MemoryStream(payload);
            using var msReader = new BinaryReader(ms, Encoding.UTF8);

            var msg = new SearchRequestMessage();
            msg.Id = (SearchRequestId)msReader.ReadByte();

            switch (msg.Id)
            {
                case SearchRequestId.Status:
                case SearchRequestId.Rebuild:
                case SearchRequestId.GetMachineSettings:
                    break;
                case SearchRequestId.SetMachineSettings:
                    msg.JsonSettings = msReader.ReadString();
                    break;
                case SearchRequestId.Search:
                    msg.Limit = msReader.ReadInt32();
                    msg.AppLimit = msReader.ReadInt32();
                    msg.Query = msReader.ReadString();
                    break;
                case SearchRequestId.SearchDir:
                    msg.Limit = msReader.ReadInt32();
                    msg.AppLimit = msReader.ReadInt32();
                    msg.DirectoryFilter = msReader.ReadString();
                    msg.Query = msReader.ReadString();
                    break;
            }
            return msg;
        }
    }
}
