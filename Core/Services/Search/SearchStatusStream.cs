using System.IO.Pipes;
using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core;

public static class SearchStatusStream
{
    public static async Task SubscribeAsync(Action<UsnIndexer.IndexerStatus> onStatus, CancellationToken token)
    {
        using var pipe = new NamedPipeClientStream(".", "SwiftListPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2000, token).ConfigureAwait(false);
        await SearchRequestBinarySerializer.WriteSearchRequestAsync(pipe, new SearchRequestMessage
        {
            Id = SearchRequestId.SubscribeStatus
        }, token).ConfigureAwait(false);

        while (!token.IsCancellationRequested && pipe.IsConnected)
        {
            var response = await PipeResponseBinarySerializer.ReadAsync(pipe, token).ConfigureAwait(false);
            if (response.Kind != PipeResponseKind.Status || response.Status == null)
                break;

            onStatus(response.Status);
        }
    }
}
