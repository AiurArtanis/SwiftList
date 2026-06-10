using System.Threading.Channels;
using SwiftList.Core;
namespace SwiftList.Service;

internal static class SearchStreamPump
{
    public static async Task RunAsync(SearchEngine? engine, SearchRequestMessage msg, Stream stream, CancellationToken token)
    {
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream, token).ConfigureAwait(false);

        var channel = Channel.CreateUnbounded<(SearchResult Result, bool IsApp)>(

            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        var producer = Task.Run(() =>
        {
            try
            {
                var directory = msg.Id == SearchRequestId.SearchDir ? msg.DirectoryFilter : null;

                engine?.SearchStreaming(msg.Query ?? string.Empty, msg.Limit, msg.AppLimit, directory,
                    (result, isApp) => channel.Writer.TryWrite((result, isApp)), token);
                channel.Writer.TryComplete();
            }

            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }

        }, token);

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                if (item.IsApp)
                    await SearchResponseBinarySerializer.WriteAppResultAsync(stream, item.Result, token).ConfigureAwait(false);
                else
                    await SearchResponseBinarySerializer.WriteFileResultAsync(stream, item.Result, token).ConfigureAwait(false);
            }

            await producer.ConfigureAwait(false);
        }

        catch (OperationCanceledException)
        {
        }

        catch (Exception ex) when (IsClientDisconnect(ex))
        {
        }

        catch (Exception ex)
        {
            Logger.Log($"[UsnService] Error processing streaming search request {msg.Id}: {ex.Message}", LogLevel.Error);
        }

        finally
        {
            try
            {
                await SearchResponseBinarySerializer.WriteEndAsync(stream, token).ConfigureAwait(false);
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
