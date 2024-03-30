using System.Buffers.Binary;

namespace MitmServerNet.Net;

/// <summary>
///     Not so efficient but it's for a proof of concept.
/// </summary>
public class HabboFrameParser
{
    private byte[] _buffer;

    public HabboFrameParser()
    {
        _buffer = Array.Empty<byte>();
    }

    public IEnumerable<HabboFrame> Parse(ReadOnlyMemory<byte> data)
    {
        // Append data to buffer.
        var buffer = new byte[_buffer.Length + data.Length];
        
        if (_buffer.Length > 0)
        {
            _buffer.CopyTo(buffer, 0);
        }
        
        data.CopyTo(buffer.AsMemory(_buffer.Length));
        
        _buffer = buffer;
        
        // Parse frames.
        var bufferOffset = 0;
        var bufferSpan = _buffer.AsMemory();

        while (bufferSpan.Length - bufferOffset >= 6)
        {
            var packetLength = BinaryPrimitives.ReadInt32BigEndian(bufferSpan.Slice(bufferOffset).Span);
            
            if (packetLength + 4 > bufferSpan.Length - bufferOffset)
            {
                break;
            }
            
            var packetData = bufferSpan.Slice(bufferOffset, packetLength + 4);
            var packetCopy = packetData.ToArray();
            
            yield return new HabboFrame(packetCopy);
            
            bufferOffset += packetLength + 4;
        }
        
        // Remove parsed frames from buffer.
        if (bufferOffset > 0)
        {
            var newBuffer = new byte[_buffer.Length - bufferOffset];
            
            _buffer.AsSpan(bufferOffset).CopyTo(newBuffer);
            _buffer = newBuffer;
        }
    }
}