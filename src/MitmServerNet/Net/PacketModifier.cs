namespace MitmServerNet.Net;

public delegate HabboPacket? PacketModifier<T>(T header, HabboPacket packet) where T : Enum;