using System.Net.Security;

namespace MitmServerNet.Net;

public class MiddleSwap<T> where T : Enum
{
    public MiddleSwap(string name, SslStream stream, HabboPacketModifier<T> modifier, ConsoleColor color)
    {
        Name = name;
        Stream = stream;
        Modifier = modifier;
        Parser = new HabboFrameParser();
        Color = color;
    }

    public string Name { get; }
    public SslStream Stream { get; }
    public HabboPacketModifier<T> Modifier { get; }
    public HabboFrameParser Parser { get; }
    public ConsoleColor Color { get; }
}