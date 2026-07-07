using System.Threading.Channels;

namespace SwiftList.Core.Services;

using SwiftList.Core;

public static class SearchStreamPump
{
    public static async Task RunAsync(SearchEngine? engine, SearchRequestMessage msg, Stream stream, CancellationToken token)
    {
        Logger.Log($"[SearchStreamPump] Starting query: '{msg.Query}', limit={msg.Limit}, appLimit={msg.AppLimit}, directoryFilter='{msg.DirectoryFilter}'", LogLevel.Debug);
        using var queryCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var queryToken = queryCts.Token;

        HashSet<byte>? disabledIds = null;
        if (msg.DisabledAliasComponents != null && msg.DisabledAliasComponents.Count > 0)
        {
            disabledIds = new HashSet<byte>();
            foreach (var comp in msg.DisabledAliasComponents)
            {
                var id = AliasProviderRegistry.GetProviderIdByComponentId(comp);
                if (id != 255)
                    disabledIds.Add(id);
            }
        }
        SearchContext.DisabledAliasIds = disabledIds;

        var bufferedStream = new BufferedStream(stream, 8192);
        try
        {
            await SearchResponseBinarySerializer.WriteHeaderAsync(bufferedStream, queryToken).ConfigureAwait(false);
            await bufferedStream.FlushAsync(queryToken).ConfigureAwait(false);

            var channel = Channel.CreateUnbounded<SearchResult>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

            var producer = Task.Run(() =>
            {
                try
                {
                    var directory = msg.Id == SearchRequestId.SearchDir ? msg.DirectoryFilter : null;

                    engine?.SearchStreaming(msg.Query ?? string.Empty, msg.Limit, msg.AppLimit, directory,
                        result => channel.Writer.TryWrite(result), queryToken);
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, queryToken);

            try
            {
                var count = 0;
                await foreach (var item in channel.Reader.ReadAllAsync(queryToken).ConfigureAwait(false))
                {
                    await SearchResponseBinarySerializer.WriteFileResultAsync(bufferedStream, item, queryToken).ConfigureAwait(false);

                    count++;
                    if (count <= 10 || count % 50 == 0)
                    {
                        await bufferedStream.FlushAsync(queryToken).ConfigureAwait(false);
                    }
                }

                await bufferedStream.FlushAsync(queryToken).ConfigureAwait(false);
                await producer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (IsClientDisconnect(ex))
            {
                queryCts.Cancel();
            }
            catch (Exception ex)
            {
                queryCts.Cancel();
                Logger.Log($"[SearchStreamPump] Error processing streaming search request {msg.Id}: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                try
                {
                    await SearchResponseBinarySerializer.WriteEndAsync(bufferedStream, token).ConfigureAwait(false);
                    await bufferedStream.FlushAsync(token).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsClientDisconnect(ex))
                {
                }
            }
        }
        finally
        {
            try
            {
                bufferedStream.Dispose();
            }
            catch (Exception ex) when (IsClientDisconnect(ex))
            {
            }
        }
    }

    private static bool IsClientDisconnect(Exception ex) => ex is EndOfStreamException ||
               ex is IOException ||
               ex.InnerException != null && IsClientDisconnect(ex.InnerException);
}
