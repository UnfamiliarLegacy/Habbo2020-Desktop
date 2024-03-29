using System.Net.Security;

namespace MitmServerNet.Net;

public record MiddleSwap(string Name, SslStream Stream, ConsoleColor Color);