using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;

namespace CozyCave;
public sealed class DesktopBridge : IDisposable
{
    private NamedPipeClientStream? pipe;
    private Cave.Transport.PipeOutbox? writer;
    public readonly ConcurrentQueue<JsonElement> Messages = new();
    public bool Connected => writer != null && pipe?.IsConnected == true;
    public async Task Connect(string name, long hwnd, string monitor)
    {
        try
        {
            pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(15000);
            writer = new Cave.Transport.PipeOutbox(pipe);
            Send(new { command = "hello", hwnd, monitor });
            using var reader = new StreamReader(pipe);
            while (await reader.ReadLineAsync() is { } line)
            { using var doc = JsonDocument.Parse(line); Messages.Enqueue(doc.RootElement.Clone()); }
        }
        catch (Exception) { }
        finally {writer?.Dispose();Messages.Enqueue(JsonSerializer.SerializeToElement(new { command = "disconnected" }));}
    }
    public void Send(object data) { try { writer?.Send(data); } catch (Exception e) when (e is IOException or ObjectDisposedException) { } }
    public void Dispose() {writer?.Dispose();pipe?.Dispose();}
}
