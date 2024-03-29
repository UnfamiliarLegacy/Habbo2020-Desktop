namespace MitmServerNet.Net;

public delegate HabboPacket? HabboPacketModifier<T>(T header, HabboPacket packet) where T : Enum;