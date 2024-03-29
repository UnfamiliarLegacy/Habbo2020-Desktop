namespace MitmServerNet.Net;

public class HabboFrame
{
    public HabboFrame(byte[] data)
    {
        Data = data;
    }
    
    public byte[] Data { get; }
}