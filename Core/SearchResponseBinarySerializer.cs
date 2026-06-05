using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SwiftList.Core
{
    public static class SearchResponseBinarySerializer
    {
        private const int Magic = 0x53524C53; // SLRS
        private const int Version = 3;
        private const byte EndFrame = 0;
        private const byte FileResultFrame = 1;
        private const byte AppResultFrame = 2;
        private const byte HeaderFrame = 255;

        public static void Write(Stream stream, SearchResponse response)
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            WriteHeader(writer);
            WriteResults(writer, response.AppResults, AppResultFrame);
            WriteResults(writer, response.FileResults, FileResultFrame);
            WriteEnd(writer);
        }

        public static BinaryWriter CreateWriter(Stream stream)
        {
            var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            WriteHeader(writer);
            return writer;
        }

        public static void WriteAppResult(BinaryWriter writer, SearchResult result)
        {
            WriteResult(writer, AppResultFrame, result);
        }

        public static void WriteFileResult(BinaryWriter writer, SearchResult result)
        {
            WriteResult(writer, FileResultFrame, result);
        }

        public static void WriteEnd(BinaryWriter writer)
        {
            writer.Write(Magic);
            writer.Write(EndFrame);
            writer.Write(0); // PayloadLength = 0
            writer.Flush();
        }

        public static SearchResponse Read(Stream stream)
        {
            var response = new SearchResponse();
            Read(stream, (result, isApp) =>
            {
                if (isApp)
                    response.AppResults.Add(result);
                else
                    response.FileResults.Add(result);
            });

            return response;
        }

        public static void Read(Stream stream, Action<SearchResult, bool> onResult)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            while (true)
            {
                int magic = reader.ReadInt32();
                if (magic != Magic)
                    throw new InvalidDataException($"Invalid search response magic: {magic:X}. Expected: {Magic:X}");

                byte frameType = reader.ReadByte();

                int length = reader.ReadInt32();
                if (length < 0 || length > 10 * 1024 * 1024) // 10MB limit
                    throw new InvalidDataException($"Invalid search response payload length: {length}");

                byte[] payload = ReadExactly(reader, length);

                if (frameType == EndFrame)
                    return;

                if (frameType == HeaderFrame)
                {
                    using var ms = new MemoryStream(payload);
                    using var msReader = new BinaryReader(ms, Encoding.UTF8);
                    int version = msReader.ReadInt32();
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
                }
                else
                {
                    throw new InvalidDataException($"Unknown search response frame: {frameType}.");
                }
            }
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
            byte[] payload = ms.ToArray();

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
            byte[] payload = ms.ToArray();

            writer.Write(Magic);
            writer.Write(frame);
            writer.Write(payload.Length);
            writer.Write(payload);
            writer.Flush();
        }

        private static SearchResult ReadResult(BinaryReader reader)
        {
            return new SearchResult
            {
                Name = reader.ReadString(),
                Path = reader.ReadString(),
                IsDir = reader.ReadBoolean(),
                Drive = reader.ReadString(),
                RankSortKey = reader.ReadUInt64()
            };
        }
    }
}
