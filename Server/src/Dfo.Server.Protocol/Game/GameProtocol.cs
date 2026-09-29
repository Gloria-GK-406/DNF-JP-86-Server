using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

internal static class GameProtocol
{
    /// <summary>
    /// The merchant a working A21 session puts on the result screen.
    /// </summary>
    /// <remarks>
    /// Two slices name it: the clear frame that opens the screen and the kill that empties the boss
    /// room. It is here rather than in either of them because a slice may not reach into another.
    /// </remarks>
    public const int SecretShopNpcId = 1000;

    public const ushort SortLockType = 0x02CA;
    public const ushort SortUnlockType = 0x02CB;

    public const ushort SortItemType = 0x0014;

    public const ushort LearnSkillType = 0x001D;

    public const ushort LoginType = 0x0001;

    public const ushort ReturnToSelectionType = 0x0007;

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

    public const ushort RequestPeerType = 0x000A;

    public const ushort ResponsePeerType = 0x000B;

    public const ushort LeavePartyType = 0x000D;

    public const ushort RemovePartyMemberType = 0x000E;

    public const ushort ChangePartyLeaderType = 0x0079;

    public const ushort CallPartyMemberRealtimeInfoType = 0x00A6;

    public const ushort SetPartyInfoType = 0x000C;

    public const ushort PartyInviteRequestNotificationType = 0x0007;

    public const ushort PartyInviteAcceptedNotificationType = 0x0008;

    public const ushort PartyRosterNotificationType = 0x0009;

    public const ushort PartyEndpointsNotificationType = 0x000B;

    public const ushort PartyRealtimeNotificationType = 0x0099;

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
    public const ushort CharacterDeathType = 0x0028;
    public const ushort CharacterLifeStateNotificationType = 0x0020;

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

    /// <summary>CMD 0x0020 <c>GIVEUP_QUEST</c>: the client abandoning a carried quest.</summary>
    public const ushort GiveUpQuestType = 0x0020;

    /// <summary>CMD 0x0021 <c>SET_QUEST_TRIGGER</c>: the client reporting that a quest moved.</summary>
    /// <remarks>
    /// The number is <c>FAIL_CLEAR_DUNGEON</c> in the notification family, which this server does
    /// not send, so only the command side has a constant. If the notification is ever needed it
    /// gets its own, the way 0x001F's two do.
    /// </remarks>
    public const ushort SetQuestTriggerType = 0x0021;

    /// <summary>CMD 0x0022 <c>FINISH_QUEST</c>: the client handing a finished quest in.</summary>
    /// <remarks>
    /// The same number as <see cref="PlayResultNotificationType"/> and a different protocol: that
    /// one is a notification this server pushes at the end of a dungeon run, this one is a command
    /// the client sends at an NPC. Two constants, for the reason 0x001F's two exist.
    /// </remarks>
    public const ushort FinishQuestType = 0x0022;

    public const ushort SecretShopNpcNotificationType = 0x0117;

    public const ushort SetPlayResultType = 0x002E;

    /// <summary>
    /// CMD 0x0075 <c>BOSS_DIE_CHECK</c>: the client's own word that the boss is down.
    /// </summary>
    /// <remarks>
    /// Sent once, immediately after the exit opens, and answered with nothing. The client's
    /// dispatch tables register a response handler for 671 command types and this is not one of
    /// them -- see <c>protocol/game/client-to-server/0x0075.md</c> for that census and for why a
    /// reply would be a frame nobody reads.
    /// </remarks>
    public const ushort BossDieCheckType = 0x0075;

    /// <summary>
    /// CMD 0x0045 <c>SCORE_SCROLL_STATE</c>: the result scroll has finished drawing.
    /// </summary>
    /// <remarks>
    /// An empty body, and the client waits on the answer: its handler for the reply is what moves
    /// the run off the result screen. Left unanswered on 2026-08-29 and the player stood in the
    /// boss room with the scroll drawn and nothing else to do.
    /// </remarks>
    public const ushort ScoreScrollStateType = 0x0045;

    /// <summary>
    /// CMD 0x0046 <c>CARD_SELECT_RIGHT_STATE</c>: the card panel's layout, asked and answered.
    /// </summary>
    /// <remarks>
    /// Two slices send under this number, which is why the message it carries is
    /// <see cref="CardSelectRightStateResponse"/> down the page rather than inside either of them.
    /// The card slice answers the client's own request for it; the result screen sends it unasked,
    /// straight after the scroll's acknowledgement, because that is where the reference server
    /// sends it and because the branch on which the client asks cannot be decided statically.
    /// </remarks>
    public const ushort CardSelectRightStateType = 0x0046;

    /// <summary>
    /// CMD 0x0047 <c>SELECT_CARD</c>: one card turned over, and every card answered.
    /// </summary>
    public const ushort SelectCardType = 0x0047;

    /// <summary>
    /// CMD 0x0048 <c>EPLP_COMMAND</c>: the button that walks a character out of a finished run.
    /// </summary>
    /// <remarks>
    /// Not <c>GIVEUP_GAME</c>. A complete reference session that cleared a dungeon and returned to
    /// town never sent <c>0x002A</c>; it sent this twice. See
    /// <c>protocol/game/client-to-server/0x0048.md</c>.
    /// </remarks>
    public const ushort EplpCommandType = 0x0048;

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

    public const ushort DeleteItemType = 0x0012;
    public const ushort BuyItemType = 0x0015;
    public const ushort SellItemType = 0x0016;
    public const ushort RepairItemType = 0x0017;
    // Selector one: the same numeric type is also UDP-host NOTI 0x001A under selector zero.
    public const ushort DisjointItemType = 0x001A;
    public const ushort ReinforceItemType = 0x0050;

    public const ushort ShopPurchaseCountType = 0x02CC;

    public const ushort UseStackableType = 0x002C;
    public const ushort QuestClearCouponType = 0x0204;

    public const ushort StoryPauseType = 0x00BF;

    public const ushort StoryPauseNotificationType = 0x00AA;

    public const ushort LoadingCompleteNotificationType = 0x001E;

    public const ushort AccountCharacterRosterNotificationType = 0x0002;

    public const ushort HonorLevelNotificationType = 0x0289;

    public const ushort AuctionServiceStateNotificationType = 0x00B7;

    public const ushort InformNoticeNotificationType = 0x01A1;

    public const ushort ConnectionCheckType = 0x04DD;
}

/// <summary>
/// CMD 0x0046 <c>CARD_SELECT_RIGHT_STATE</c>: the card panel's layout, seventeen bytes of it.
/// </summary>
/// <remarks>
/// It names eligible party seats, not rewards. The captured single-player body marks seat zero;
/// a party marks each current member in the same eight-word layout. See
/// <c>Game/CardReward/CardRewardPayloadCodec.cs</c>.
/// <para>
/// It lives at the endpoint layer rather than in the card slice for the reason
/// <see cref="GameProtocol.SecretShopNpcId"/> does: two slices name it. The card slice answers the
/// client's request for the layout, and the result screen sends it after acknowledging the scroll,
/// and a slice may not reach into another.
/// </para>
/// </remarks>
public sealed record CardSelectRightStateResponse(IReadOnlyList<byte>? EligibleSeats = null)
    : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCardSelectRightState;
}
