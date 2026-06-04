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
            writer.Flush();
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
            writer.Write(EndFrame);
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
            int magic = reader.ReadInt32();
            if (magic != Magic)
                throw new InvalidDataException("Invalid search response binary header.");

            int version = reader.ReadInt32();
            if (version != Version)
                throw new InvalidDataException($"Unsupported search response binary version: {version}.");

            while (true)
            {
                byte frame = reader.ReadByte();
                if (frame == EndFrame)
                    return;

                var result = ReadResult(reader);
                if (frame == FileResultFrame)
                    onResult(result, false);
                else if (frame == AppResultFrame)
                    onResult(result, true);
                else
                    throw new InvalidDataException($"Unknown search response frame: {frame}.");
            }
        }

        private static void WriteResults(BinaryWriter writer, List<SearchResult> results, byte frame)
        {
            foreach (var result in results)
                WriteResult(writer, frame, result);
        }

        private static void WriteHeader(BinaryWriter writer)
        {
            writer.Write(Magic);
            writer.Write(Version);
        }

        private static void WriteResult(BinaryWriter writer, byte frame, SearchResult result)
        {
            writer.Write(frame);
            writer.Write(result.Name);
            writer.Write(result.Path);
            writer.Write(result.IsDir);
            writer.Write(result.Drive);
            writer.Write(result.RankSortKey);
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
