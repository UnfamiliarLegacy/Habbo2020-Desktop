using System.Buffers.Binary;
using System.Text;

namespace MitmServerNet.Net;

public class HabboPacket
{
    private int _offset = 0;
    
    public HabboPacket(HabboFrame frame, bool skipShuffle = false)
    {
        _offset = 6;
        
        var dataSpan = frame.Data.AsSpan();

        Frame = frame;
        SkipShuffle = skipShuffle;
        Length = BinaryPrimitives.ReadInt32BigEndian(dataSpan);
        Id = BinaryPrimitives.ReadInt16BigEndian(dataSpan.Slice(4));
    }
    
    public HabboFrame Frame { get; }
    public bool SkipShuffle { get; }
    public int Length { get; }
    public short Id { get; }

    public bool ReadBoolean()
    {
        return Frame.Data[_offset++] == 1;
    }

    public string ReadString()
    {
        var length = Frame.Data[_offset] << 8 | Frame.Data[_offset + 1];
        _offset += 2;
        
        var value = Encoding.UTF8.GetString(Frame.Data, _offset, length);
        _offset += length;
        
        return value;
    }
}