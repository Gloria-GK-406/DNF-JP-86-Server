namespace Dfo.Server.Protocol.Game;

internal static class GameProtocol
{
    public const ushort LoginType = 0x0001;

    public const ushort ExitType = 0x0003;

    public const ushort SelectCharacterType = 0x0004;

    public const ushort GetUserInfoType = 0x0008;

    public const ushort ClientSpecStatisticType = 0x00AB;

    public const ushort ClientHardwareDetailType = 0x01DD;

    public const ushort ClientSpecProfileType = 0x00C2;

    public const ushort SessionMetricsType = 0x00FA;

    public const ushort TaggedIdReportType = 0x008F;

    public const ushort RosterFollowUpType = 0x02A8;

    public const ushort AccountCharacterRosterNotificationType = 0x0002;
}
