using System.Net.Security;

namespace MitmServerNet.Net;

public class MiddleSwap<T> where T : Enum
{
    public MiddleSwap(string name, SslStream stream, PacketModifier<T> modifier, PacketShuffler shuffleIn, PacketShuffler shuffleOut, ConsoleColor color)
    {
        Name = name;
        Stream = stream;
        Modifier = modifier;
        ShuffleIn = shuffleIn;
        ShuffleOut = shuffleOut;
        Parser = new HabboFrameParser();
        Color = color;
    }

    public string Name { get; }
    public SslStream Stream { get; }
    public PacketModifier<T> Modifier { get; }
    public PacketShuffler ShuffleIn { get; }
    public PacketShuffler ShuffleOut { get; }
    public HabboFrameParser Parser { get; }
    public ConsoleColor Color { get; }
}