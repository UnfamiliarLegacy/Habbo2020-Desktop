using System.Net.WebSockets;

namespace MitmServerNet.Net;

public class WebsocketStream : Stream
{
    private readonly WebSocket _websocket;

    public WebsocketStream(WebSocket websocket)
    {
        _websocket = websocket;
    }

    public override bool CanRead => _websocket.State == WebSocketState.Open;
    public override bool CanWrite => _websocket.State == WebSocketState.Open;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new())
    {
        var recv = await _websocket.ReceiveAsync(buffer, cancellationToken);
        return recv.Count;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotImplementedException();
    }

    public override void SetLength(long value)
    {
        throw new NotImplementedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = new())
    {
        await _websocket.SendAsync(buffer, WebSocketMessageType.Binary, true, cancellationToken);
    }

    public override void Flush()
    {
        throw new NotImplementedException();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        // no-op.
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }
}