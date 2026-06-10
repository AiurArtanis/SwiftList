using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftList.Core.Extensions
{
    public static class StreamExtensions
    {
        public static async Task<int> ReadInt32Async(this Stream stream, CancellationToken token)
        {
            byte[] bytes = await stream.ReadExactlyAsync(sizeof(int), token).ConfigureAwait(false);
            return BitConverter.ToInt32(bytes, 0);
        }

        public static async Task<byte[]> ReadExactlyAsync(this Stream stream, int count, CancellationToken token)
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
