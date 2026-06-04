using System;
using System.IO;
using System.Text;

namespace SwiftList.Core
{
    public static class PipeRequestBinarySerializer
    {
        private const int Magic = 0x51504C53; // SLPQ
        private const int VersionLegacyString = 1;
        private const int VersionIpc = 2;
        private const int VersionSearchRequest = 3;

        // --- Legacy String Protocol (Used by AppPipeService for single instance activation) ---

        public static void Write(Stream stream, string command)
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(VersionLegacyString);
            writer.Write(command ?? string.Empty);
            writer.Flush();
        }

        public static string Read(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            int magic = reader.ReadInt32();
            if (magic != Magic)
                throw new InvalidDataException("Invalid pipe request binary header.");

            int version = reader.ReadInt32();
            if (version != VersionLegacyString)
                throw new InvalidDataException($"Unsupported legacy string pipe request version: {version}.");

            return reader.ReadString();
        }

        // --- Structured Binary IPC Protocol (Used by Hook client and server) ---

        public static void WriteMessage(Stream stream, IpcMessage msg)
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(VersionIpc);
            writer.Write((byte)msg.Id);

            switch (msg.Id)
            {
                case IpcMessageId.Stop:
                    break;
                case IpcMessageId.SetAppProcessId:
                    writer.Write(msg.ProcessId);
                    break;
                case IpcMessageId.SetQuickSearchVisible:
                case IpcMessageId.SetInlineSearchVisible:
                case IpcMessageId.SetHotkeysDisabled:
                    writer.Write(msg.BoolVal);
                    break;
                case IpcMessageId.NavigateDialog:
                    writer.Write(msg.Hwnd);
                    writer.Write(msg.StringVal1 ?? string.Empty);
                    break;
                case IpcMessageId.RestoreDialogFocus:
                    writer.Write(msg.Hwnd);
                    break;
                case IpcMessageId.Activate:
                case IpcMessageId.ExplorerDeactivated:
                case IpcMessageId.ActiveWindowMoved:
                case IpcMessageId.KeyBackspace:
                case IpcMessageId.KeyEscape:
                case IpcMessageId.KeyEnter:
                case IpcMessageId.KeyUp:
                case IpcMessageId.KeyDown:
                case IpcMessageId.KeyLeft:
                case IpcMessageId.KeyRight:
                    break;
                case IpcMessageId.KeyChar:
                    writer.Write(msg.CharVal);
                    break;
                case IpcMessageId.KeyCtrlNumber:
                    writer.Write(msg.IntVal);
                    break;
                case IpcMessageId.MouseClick:
                    writer.Write(msg.MouseX);
                    writer.Write(msg.MouseY);
                    break;
                case IpcMessageId.ExplorerActivated:
                    writer.Write(msg.Hwnd);
                    writer.Write(msg.StringVal1 ?? string.Empty);
                    writer.Write(msg.StringVal2 ?? string.Empty);
                    writer.Write(msg.IsDesktop);
                    break;
                case IpcMessageId.PathCaptured:
                    writer.Write(msg.StringVal1 ?? string.Empty);
                    writer.Write(msg.IsDesktop);
                    break;
                case IpcMessageId.Error:
                    writer.Write(msg.StringVal1 ?? string.Empty);
                    break;
            }
            writer.Flush();
        }

        public static IpcMessage ReadMessage(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            int magic = reader.ReadInt32();
            if (magic != Magic)
                throw new InvalidDataException("Invalid pipe request binary header.");

            int version = reader.ReadInt32();
            if (version != VersionIpc)
                throw new InvalidDataException($"Unsupported pipe request binary version for structured IPC: {version}.");

            var msg = new IpcMessage();
            msg.Id = (IpcMessageId)reader.ReadByte();

            switch (msg.Id)
            {
                case IpcMessageId.Stop:
                    break;
                case IpcMessageId.SetAppProcessId:
                    msg.ProcessId = reader.ReadUInt32();
                    break;
                case IpcMessageId.SetQuickSearchVisible:
                case IpcMessageId.SetInlineSearchVisible:
                case IpcMessageId.SetHotkeysDisabled:
                    msg.BoolVal = reader.ReadBoolean();
                    break;
                case IpcMessageId.NavigateDialog:
                    msg.Hwnd = reader.ReadInt64();
                    msg.StringVal1 = reader.ReadString();
                    break;
                case IpcMessageId.RestoreDialogFocus:
                    msg.Hwnd = reader.ReadInt64();
                    break;
                case IpcMessageId.Activate:
                case IpcMessageId.ExplorerDeactivated:
                case IpcMessageId.ActiveWindowMoved:
                case IpcMessageId.KeyBackspace:
                case IpcMessageId.KeyEscape:
                case IpcMessageId.KeyEnter:
                case IpcMessageId.KeyUp:
                case IpcMessageId.KeyDown:
                case IpcMessageId.KeyLeft:
                case IpcMessageId.KeyRight:
                    break;
                case IpcMessageId.KeyChar:
                    msg.CharVal = reader.ReadChar();
                    break;
                case IpcMessageId.KeyCtrlNumber:
                    msg.IntVal = reader.ReadInt32();
                    break;
                case IpcMessageId.MouseClick:
                    msg.MouseX = reader.ReadInt32();
                    msg.MouseY = reader.ReadInt32();
                    break;
                case IpcMessageId.ExplorerActivated:
                    msg.Hwnd = reader.ReadInt64();
                    msg.StringVal1 = reader.ReadString();
                    msg.StringVal2 = reader.ReadString();
                    msg.IsDesktop = reader.ReadBoolean();
                    break;
                case IpcMessageId.PathCaptured:
                    msg.StringVal1 = reader.ReadString();
                    msg.IsDesktop = reader.ReadBoolean();
                    break;
                case IpcMessageId.Error:
                    msg.StringVal1 = reader.ReadString();
                    break;
            }
            return msg;
        }

        // --- Structured Binary Search Request Protocol (Used by SearchService and UsnServicePipeServer) ---

        public static void WriteSearchRequest(Stream stream, SearchRequestMessage msg)
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(VersionSearchRequest);
            writer.Write((byte)msg.Id);

            switch (msg.Id)
            {
                case SearchRequestId.Status:
                case SearchRequestId.Rebuild:
                case SearchRequestId.GetMachineSettings:
                    break;
                case SearchRequestId.SetMachineSettings:
                    writer.Write(msg.JsonSettings ?? string.Empty);
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

            var msg = new SearchRequestMessage();
            msg.Id = (SearchRequestId)reader.ReadByte();

            switch (msg.Id)
            {
                case SearchRequestId.Status:
                case SearchRequestId.Rebuild:
                case SearchRequestId.GetMachineSettings:
                    break;
                case SearchRequestId.SetMachineSettings:
                    msg.JsonSettings = reader.ReadString();
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
    }
}
