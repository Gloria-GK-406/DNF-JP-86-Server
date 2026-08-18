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

    public const ushort CreateCharacterType = 0x0005;

    public const ushort DeleteCharacterType = 0x0006;

    public const ushort MercenaryInfoType = 0x01BA;

    public const ushort HiddenCharacterInfoType = 0x0286;

    public const ushort CheckCharacterNameType = 0x02B5;

    public const ushort SaveGameOption1Type = 0x00C5;

    public const ushort ContentsPlayInfoType = 0x02C1;

    public const ushort SetUdpIpPortType = 0x0002;

    public const ushort CeraQueryType = 0x003F;

    public const ushort SetUserPositionType = 0x0023;

    public const ushort SetUserAreaType = 0x0024;

    public const ushort AreaUsersNotificationType = 0x0018;

    public const ushort InformNoticeSecondType = 0x01F8;

    public const ushort SecurityStatusType = 0x0252;

    public const ushort RuntimeTingStatisticType = 0x02B3;

    public const ushort EnterSelectDungeonType = 0x000F;

    public const ushort UserStateNotificationType = 0x0003;

    public const ushort UserAreaNotificationType = 0x0017;

    public const ushort UdpHostNotificationType = 0x001A;

    public const ushort EnterSelectDungeonNotificationType = 0x001B;

    public const ushort SelectDungeonType = 0x0010;

    public const ushort DungeonInfoNotificationType = 0x001C;

    public const ushort StartMapNotificationType = 0x001D;

    public const ushort SupportTagNotificationType = 0x019F;

    public const ushort FinishLoadingType = 0x0025;

    public const ushort MoveMapType = 0x002D;

    public const ushort MoveItemSpaceType = 0x0013;

    public const ushort UseStackableType = 0x002C;

    public const ushort StoryPauseType = 0x00BF;

    public const ushort StoryPauseNotificationType = 0x00AA;

    public const ushort LoadingCompleteNotificationType = 0x001E;

    public const ushort AccountCharacterRosterNotificationType = 0x0002;

    public const ushort HonorLevelNotificationType = 0x0289;

    public const ushort AuctionServiceStateNotificationType = 0x00B7;

    public const ushort InformNoticeNotificationType = 0x01A1;
}
