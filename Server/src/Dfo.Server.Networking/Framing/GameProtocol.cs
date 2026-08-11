namespace Dfo.Server.Networking.Framing;

internal static class GameProtocol
{
    public const byte Command = 0x01;

    public const ushort LoginType = 0x0001;

    public const ushort ExitType = 0x0003;

    public const ushort GetUserInfoType = 0x0008;

    public const ushort AccountCharacterRosterNotificationType = 0x0002;
}
