using System.IO;
using System.Text.Json;
using System.Threading.Channels;
namespace Cave.Transport;
// One bounded writer owns the stream. UI threads never wait for the peer.
public sealed class PipeOutbox : IDisposable
{
    private readonly Channel<string> queue=Channel.CreateBounded<string>(new BoundedChannelOptions(128){SingleReader=true,FullMode=BoundedChannelFullMode.Wait});
    private readonly Stream stream;
    private readonly CancellationTokenSource stop=new();
    public PipeOutbox(Stream stream) {this.stream=stream;_ = Task.Run(Pump);}
    public void Send(object message)
    {
        if(!queue.Writer.TryWrite(JsonSerializer.Serialize(message))) Dispose();
    }
    private async Task Pump()
    {
        try
        {
            var writer=new StreamWriter(stream,leaveOpen:true);
            await foreach(var line in queue.Reader.ReadAllAsync(stop.Token))
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(3000);
                await writer.WriteLineAsync(line.AsMemory(),timeout.Token).ConfigureAwait(false);
                await writer.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
        }
        catch(Exception e) when(e is IOException or ObjectDisposedException or OperationCanceledException) {Dispose();}
    }
    public void Dispose() {queue.Writer.TryComplete();stop.Cancel();stream.Dispose();}
}
