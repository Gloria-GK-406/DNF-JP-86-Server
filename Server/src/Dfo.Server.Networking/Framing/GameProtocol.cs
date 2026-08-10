namespace Dfo.Server.Networking.Framing;

internal static class GameProtocol
{
    public const byte Command = 0x01;

    public const ushort LoginType = 0x0001;

    public const ushort ExitType = 0x0003;
}
