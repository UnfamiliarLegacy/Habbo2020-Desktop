using System.Text;

namespace MitmServerNet.Net;

public class HabboPacketWriter
{
    private int _offset;
    private byte[] _buffer;

    public HabboPacketWriter(short id)
    {
        Id = id;
        _buffer = Array.Empty<byte>();
        
        WriteInt(0);
        WriteShort(id);
    }
    
    public short Id { get; }
    public byte[] Buffer => _buffer;
    
    // Resize the buffer to the specified length.
    private void IncreaseBuffer(int size)
    {
        // Resize buffer.
        Array.Resize(ref _buffer, _buffer.Length + size);
        
        // Update length.
        var length = _buffer.Length - 4;
        
        _buffer[0] = (byte)(length >> 24);
        _buffer[1] = (byte)(length >> 16);
        _buffer[2] = (byte)(length >> 8);
        _buffer[3] = (byte)length;
    }

    public void WriteBoolean(bool b)
    {
        IncreaseBuffer(1);
        
        _buffer[_offset++] = (byte)(b ? 1 : 0);
    }
    
    public void WriteShort(short value)
    {
        IncreaseBuffer(2);
        
        _buffer[_offset++] = (byte)(value >> 8);
        _buffer[_offset++] = (byte)value;
    }
    
    public void WriteInt(int value)
    {
        IncreaseBuffer(4);
        
        _buffer[_offset++] = (byte)(value >> 24);
        _buffer[_offset++] = (byte)(value >> 16);
        _buffer[_offset++] = (byte)(value >> 8);
        _buffer[_offset++] = (byte)value;
    }
    
    public void WriteString(string value)
    {
        // Write length.
        var length = Encoding.UTF8.GetByteCount(value);
        
        WriteShort((short)length);
        
        // Write string.
        IncreaseBuffer(length);
        Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _offset);
        _offset += length;
    }
}