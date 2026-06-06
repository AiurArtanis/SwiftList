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

        // --- Legacy String Protocol (Used by AppPipeService for single instance activation) ---

        public static void Write(Stream stream, string command)
        {
            using var ms = new MemoryStream();
            using (var msWriter = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                msWriter.Write(command ?? string.Empty);
            }
            byte[] payload = ms.ToArray();

            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(VersionLegacyString);
            writer.Write(payload.Length);
            writer.Write(payload);
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

            int length = reader.ReadInt32();
            if (length < 0 || length > 10 * 1024 * 1024)
                throw new InvalidDataException($"Invalid legacy payload length: {length}");

            byte[] payload = ReadExactly(reader, length);
            using var ms = new MemoryStream(payload);
            using var msReader = new BinaryReader(ms, Encoding.UTF8);
            return msReader.ReadString();
        }

        // --- Structured Binary IPC Protocol (Used by Hook client and server) ---

        public static void WriteMessage(BinaryWriter writer, IpcMessage msg)
        {
            using var ms = new MemoryStream();
            using (var msWriter = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                msWriter.Write((byte)msg.Id);

                switch (msg.Id)
                {
                    case IpcMessageId.Stop:
                        break;
                    case IpcMessageId.SetAppProcessId:
                        msWriter.Write(msg.ProcessId);
                        break;
                    case IpcMessageId.SetQuickSearchVisible:
                    case IpcMessageId.SetInlineSearchVisible:
                    case IpcMessageId.SetHotkeysDisabled:
                        msWriter.Write(msg.BoolVal);
                        break;
                    case IpcMessageId.NavigateDialog:
                        msWriter.Write(msg.Hwnd);
                        msWriter.Write(msg.StringVal1 ?? string.Empty);
                        break;
                    case IpcMessageId.RestoreDialogFocus:
                        msWriter.Write(msg.Hwnd);
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
                        msWriter.Write(msg.CharVal);
                        break;
                    case IpcMessageId.KeyCtrlNumber:
                        msWriter.Write(msg.IntVal);
                        break;
                    case IpcMessageId.MouseClick:
                        msWriter.Write(msg.MouseX);
                        msWriter.Write(msg.MouseY);
                        break;
                    case IpcMessageId.ExplorerActivated:
                        msWriter.Write(msg.Hwnd);
                        msWriter.Write(msg.StringVal1 ?? string.Empty);
                        msWriter.Write(msg.StringVal2 ?? string.Empty);
                        msWriter.Write(msg.IsDesktop);
                        break;
                    case IpcMessageId.PathCaptured:
                        msWriter.Write(msg.StringVal1 ?? string.Empty);
                        msWriter.Write(msg.IsDesktop);
                        break;
                    case IpcMessageId.Error:
                        msWriter.Write(msg.StringVal1 ?? string.Empty);
                        break;
                    case IpcMessageId.GetListItems:
                        msWriter.Write(msg.Hwnd);
                        break;
                    case IpcMessageId.SelectItem:
                        msWriter.Write(msg.Hwnd);
                        msWriter.Write(msg.StringVal1 ?? string.Empty);
                        msWriter.Write(msg.IntVal);
                        msWriter.Write(msg.BoolVal);
                        msWriter.Write(msg.IsDesktop);
                        break;
                    case IpcMessageId.ClearSelection:
                        msWriter.Write(msg.Hwnd);
                        msWriter.Write(msg.StringVal1 ?? string.Empty);
                        break;
                    case IpcMessageId.GetSelectedIndices:
                        msWriter.Write(msg.Hwnd);
                        msWriter.Write(msg.StringVal1 ?? string.Empty);
                        break;
                    case IpcMessageId.GetListItemsResponse:
                        if (msg.StringArray != null)
                        {
                            msWriter.Write(msg.StringArray.Length);
                            foreach (var s in msg.StringArray)
                            {
                                msWriter.Write(s ?? string.Empty);
                            }
                        }
                        else
                        {
                            msWriter.Write(0);
                        }
                        break;
                    case IpcMessageId.GetSelectedIndicesResponse:
                        if (msg.IntArray != null)
                        {
                            msWriter.Write(msg.IntArray.Length);
                            foreach (var val in msg.IntArray)
                            {
                                msWriter.Write(val);
                            }
                        }
                        else
                        {
                            msWriter.Write(0);
                        }
                        break;
                }
            }
            byte[] payload = ms.ToArray();

            writer.Write(Magic);
            writer.Write(VersionIpc);
            writer.Write(payload.Length);
            writer.Write(payload);
            writer.Flush();
        }

        public static IpcMessage ReadMessage(BinaryReader reader)
        {
            int magic = reader.ReadInt32();
            if (magic != Magic)
                throw new InvalidDataException("Invalid pipe request binary header.");

            int version = reader.ReadInt32();
            if (version != VersionIpc)
                throw new InvalidDataException($"Unsupported pipe request binary version for structured IPC: {version}.");

            int length = reader.ReadInt32();
            if (length < 0 || length > 10 * 1024 * 1024)
                throw new InvalidDataException($"Invalid IPC payload length: {length}");

            byte[] payload = ReadExactly(reader, length);
            using var ms = new MemoryStream(payload);
            using var msReader = new BinaryReader(ms, Encoding.UTF8);

            var msg = new IpcMessage();
            msg.Id = (IpcMessageId)msReader.ReadByte();

            switch (msg.Id)
            {
                case IpcMessageId.Stop:
                    break;
                case IpcMessageId.SetAppProcessId:
                    msg.ProcessId = msReader.ReadUInt32();
                    break;
                case IpcMessageId.SetQuickSearchVisible:
                case IpcMessageId.SetInlineSearchVisible:
                case IpcMessageId.SetHotkeysDisabled:
                    msg.BoolVal = msReader.ReadBoolean();
                    break;
                case IpcMessageId.NavigateDialog:
                    msg.Hwnd = msReader.ReadInt64();
                    msg.StringVal1 = msReader.ReadString();
                    break;
                case IpcMessageId.RestoreDialogFocus:
                    msg.Hwnd = msReader.ReadInt64();
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
                    msg.CharVal = msReader.ReadChar();
                    break;
                case IpcMessageId.KeyCtrlNumber:
                    msg.IntVal = msReader.ReadInt32();
                    break;
                case IpcMessageId.MouseClick:
                    msg.MouseX = msReader.ReadInt32();
                    msg.MouseY = msReader.ReadInt32();
                    break;
                case IpcMessageId.ExplorerActivated:
                    msg.Hwnd = msReader.ReadInt64();
                    msg.StringVal1 = msReader.ReadString();
                    msg.StringVal2 = msReader.ReadString();
                    msg.IsDesktop = msReader.ReadBoolean();
                    break;
                case IpcMessageId.PathCaptured:
                    msg.StringVal1 = msReader.ReadString();
                    msg.IsDesktop = msReader.ReadBoolean();
                    break;
                case IpcMessageId.Error:
                    msg.StringVal1 = msReader.ReadString();
                    break;
                case IpcMessageId.GetListItems:
                    msg.Hwnd = msReader.ReadInt64();
                    break;
                case IpcMessageId.SelectItem:
                    msg.Hwnd = msReader.ReadInt64();
                    msg.StringVal1 = msReader.ReadString();
                    msg.IntVal = msReader.ReadInt32();
                    msg.BoolVal = msReader.ReadBoolean();
                    msg.IsDesktop = msReader.ReadBoolean();
                    break;
                case IpcMessageId.ClearSelection:
                    msg.Hwnd = msReader.ReadInt64();
                    msg.StringVal1 = msReader.ReadString();
                    break;
                case IpcMessageId.GetSelectedIndices:
                    msg.Hwnd = msReader.ReadInt64();
                    msg.StringVal1 = msReader.ReadString();
                    break;
                case IpcMessageId.GetListItemsResponse:
                    {
                        int count = msReader.ReadInt32();
                        var arr = new string[count];
                        for (int i = 0; i < count; i++)
                        {
                            arr[i] = msReader.ReadString();
                        }
                        msg.StringArray = arr;
                    }
                    break;
                case IpcMessageId.GetSelectedIndicesResponse:
                    {
                        int count = msReader.ReadInt32();
                        var arr = new int[count];
                        for (int i = 0; i < count; i++)
                        {
                            arr[i] = msReader.ReadInt32();
                        }
                        msg.IntArray = arr;
                    }
                    break;
            }
            return msg;
        }
    }
}
