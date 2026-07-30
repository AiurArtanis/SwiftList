using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading.Channels;

using SwiftList.Core.Wire;
using SwiftList.Core.SearchIndex;
namespace SwiftList.Core.Services.Search;

using SwiftList.Core;

public static class SearchStreamPump
{
    public static async Task RunAsync(SearchEngine? engine, SearchRequestMessage msg, Stream stream, CancellationToken token)
    {
        Logger.Log($"[SearchStreamPump] Starting query: '{msg.Query}', limit={msg.Limit}, appLimit={msg.AppLimit}, directoryFilter='{msg.DirectoryFilter}'", LogLevel.Debug);
        using var queryCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var queryToken = queryCts.Token;

        // A long-running search scan (broad/short query over a large index) holds the pipe idle on the
        // server side with no read or write in flight, so a client that gives up and disconnects (types
        // another character, cancelling this request) goes completely unnoticed until this method tries
        // to write the response back -- by then the scan has already run to full completion for nothing.
        // PeekNamedPipe queries the OS connection state directly without consuming/blocking on the data
        // stream, so this can detect that disconnect WHILE the scan is still running and cancel it early.
        using var watchdogStopCts = new CancellationTokenSource();
        _ = WatchForClientDisconnectAsync(stream, queryCts, watchdogStopCts.Token);

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
        // The service runs as a different identity and cannot read the calling user's settings file,
        // so this preference only exists here as whatever the request carried over the pipe.
        SearchContext.FuzzyMatchEnabled = !msg.ExactMatch;

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

            var streamed = 0;
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
                streamed = count;
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

            ReclaimAfterLargeSearch(streamed);
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
            finally
            {
                watchdogStopCts.Cancel();
            }
        }
    }

    // Polls PeekNamedPipe on the raw pipe handle every 25ms and cancels `queryCts` the moment the OS
    // reports the connection is gone -- lets an abandoned scan (see the comment at the call site) abort
    // between chunks instead of always running to completion. No-ops for a non-pipe stream (e.g. tests).
    private static async Task WatchForClientDisconnectAsync(Stream stream, CancellationTokenSource queryCts, CancellationToken stopToken)
    {
        if (stream is not NamedPipeServerStream pipe)
            return;

        var handle = pipe.SafePipeHandle;
        try
        {
            while (!stopToken.IsCancellationRequested)
            {
                await Task.Delay(25, stopToken).ConfigureAwait(false);
                if (handle.IsClosed || handle.IsInvalid)
                    return;

                if (!Win32Api.PeekNamedPipe(handle, IntPtr.Zero, 0, IntPtr.Zero, out _, IntPtr.Zero) &&
                    Marshal.GetLastWin32Error() == Win32Api.ERROR_BROKEN_PIPE)
                {
                    queryCts.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    // A whole-drive query leaves behind one SearchResult per match, plus the strings in it and the
    // channel that carried them, and every one of those is unreachable the moment it has been written to
    // the pipe. Nothing else in this process allocates while it waits for the next request, so the
    // collector is never provoked and the working set simply stays wherever the biggest search left it --
    // reported as the service sitting on 1.1GB indefinitely after one search for "a", and dropping only
    // once some later query happened to allocate enough to trigger a collection on its own.
    //
    // Measured on 600k results: 247MB of working set down to 58MB, in 37ms. Compacting because the
    // results are large enough to have gone onto the large object heap, which is not compacted by
    // default, so releasing it without that leaves the address space just as fragmented.
    //
    // Deliberately after the End frame has been written and flushed: the client already has everything
    // and is not waiting on this. Only for searches big enough to be worth it -- an ordinary keystroke
    // returns a few hundred rows and should not pay for a gen2 pause.
    private const int ReclaimAfterResultCount = 100_000;

    // Separated from the collection itself so the threshold -- the promise that an ordinary keystroke
    // never pays for a gen2 pause -- can be stated and tested without running one.
    internal static bool ShouldReclaimAfter(int streamedResults) => streamedResults >= ReclaimAfterResultCount;

    private static void ReclaimAfterLargeSearch(int streamedResults)
    {
        if (!ShouldReclaimAfter(streamedResults))
            return;

        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private static bool IsClientDisconnect(Exception ex) => ex is EndOfStreamException ||
               ex is IOException ||
               ex.InnerException != null && IsClientDisconnect(ex.InnerException);
}
