using System.Buffers.Binary;

namespace MitmServerNet.Net;

public class HabboPacket
{
    private int _offset = 0;
    
    public HabboPacket(HabboFrame frame)
    {
        _offset = 6;
        
        var dataSpan = frame.Data.AsSpan();

        Frame = frame;
        Length = BinaryPrimitives.ReadInt32BigEndian(dataSpan);
        Id = BinaryPrimitives.ReadInt16BigEndian(dataSpan.Slice(4));
    }
    
    public HabboFrame Frame { get; }
    public int Length { get; }
    public short Id { get; }
}