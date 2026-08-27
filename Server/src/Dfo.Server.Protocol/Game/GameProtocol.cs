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

    /// <summary>NOTI 0x0016 <c>USER_POSITION</c>, which A21 sends back for every 0x0023.</summary>
    public const ushort UserPositionNotificationType = 0x0016;

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

    /// <summary>The client reporting a room actor dead, and the two frames that answer it.</summary>
    public const ushort MonsterDeathType = 0x0027;

    public const ushort ExperienceNotificationType = 0x0025;

    public const ushort MonsterDeathNotificationType = 0x0026;

    /// <summary>Picking an item up off the floor, and the notification that answers it.</summary>
    public const ushort GetItemType = 0x002B;

    public const ushort GroundPickupNotificationType = 0x0027;

    /// <summary>The exit, the merchant behind it, and the two frames of the result screen.</summary>
    public const ushort EnableClearDungeonNotificationType = 0x001F;

    /// <summary>CMD 0x001F <c>ACCEPT_QUEST</c>: the client taking a quest on.</summary>
    /// <remarks>
    /// The same number as <see cref="EnableClearDungeonNotificationType"/> and a different
    /// protocol: that one is a notification this server pushes, this one is a command the client
    /// sends. The two families share one number space and disagree about most of it, so they get
    /// two constants rather than one.
    /// </remarks>
    public const ushort AcceptQuestType = 0x001F;

    public const ushort SecretShopNpcNotificationType = 0x0117;

    public const ushort SetPlayResultType = 0x002E;

    public const ushort PlayResultNotificationType = 0x0022;

    public const ushort ClearDungeonRewardNotificationType = 0x0023;

    /// <summary>The tutorial's own level grant, asked and answered under one type.</summary>
    public const ushort TutorialLevelUpType = 0x01E4;

    /// <summary>The types the reference server answers with nothing. See Game/UnansweredReports.</summary>
    public const ushort UseSkillType = 0x0026;

    public const ushort SyncItemSpaceType = 0x035C;

    public const ushort DecreaseDurabilityType = 0x0030;

    public const ushort OverflowInfoType = 0x00D9;

    public const ushort LogDomainConnectType = 0x04F1;

    public const ushort LogConnectProcessType = 0x04D7;

    public const ushort CharacterStatisticType = 0x007B;

    public const ushort RequestBlacklistType = 0x0078;

    public const ushort FarmEventActionType = 0x02BE;

    public const ushort PiType = 0x011B;

    public const ushort RequestIgaVersionType = 0x039D;

    public const ushort FpsPerformanceStatisticType = 0x02EF;

    public const ushort FrameLagPerDungeonType = 0x045C;

    public const ushort LagStatisticsType = 0x0117;

    /// <summary>Leaving the dungeon, asked and answered under one type.</summary>
    public const ushort GiveUpGameType = 0x002A;

    /// <summary>Closing the dungeon-selection screen, asked and answered under one type.</summary>
    public const ushort BackToVillageType = 0x0084;

    public const ushort MoveItemSpaceType = 0x0013;

    public const ushort UseStackableType = 0x002C;

    public const ushort StoryPauseType = 0x00BF;

    public const ushort StoryPauseNotificationType = 0x00AA;

    public const ushort LoadingCompleteNotificationType = 0x001E;

    public const ushort AccountCharacterRosterNotificationType = 0x0002;

    public const ushort HonorLevelNotificationType = 0x0289;

    public const ushort AuctionServiceStateNotificationType = 0x00B7;

    public const ushort InformNoticeNotificationType = 0x01A1;

    public const ushort ConnectionCheckType = 0x04DD;
}
