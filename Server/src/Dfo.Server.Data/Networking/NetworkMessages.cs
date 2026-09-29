namespace Dfo.Server.Data.Networking;

public enum ClientMessageType
{
    GameMailboxSend = 0x0001_005E,
    GameMailboxSendMulti = 0x0001_013B,
    GameMailboxClaim = 0x0001_005F,
    GameMailboxAction = 0x0001_0086,
    GameMailboxOpen = 0x0001_0060,
    GameMailboxQuery = 0x0001_0144,
    GamePremiumServiceUsageReport = 0x0001_036F,
    GameMagicBoxBatchOpening = 0x0001_0450,
    GameMagicBoxSingleOpening = 0x0001_00D0,
    GameVendingMachine = 0x0001_00D7,
    GameLotteryOpening = 0x0001_001B,
    GamePackageOpening = 0x0001_00A0,
    GameAvatarPackageOpening = 0x0001_0207,
    GameGenCeraTicket = 0x0001_0041,
    GameCeraShopPurchase = 0x0001_0040,
    GameBuyItem = 0x0001_0015,
    GameSellItem = 0x0001_0016,
    GameRepairItem = 0x0001_0017,
    GameRecoverStamina = 0x0001_0009,
    GameDisjointItem = 0x0001_001A,
    GameReinforceItem = 0x0001_0050,
    GameDeleteItem = 0x0001_0012,
    GameCrystalContract = 0x0001_0218,
    GameLearnSkill = 0x0001_001D,
    GameChangeSkillSlot = 0x0001_001C,
    GameSwitchSkillBuild = 0x0001_0104,
    GameShopPurchaseCount = 0x0001_02CC,
    GameLogin = 0x0001_0001,
    GameGetAccountCharacterRoster = 0x0001_0208,
    // Synthetic catalogue identity for GET_USERINFO mode 3. The wire key remains CMD 0x0008;
    // keeping the semantic query distinct prevents its target from entering the account-roster
    // handler merely because both share one client opcode.
    GameGetOtherCharacterDetail = 0x0001_0308,
    GameSelectCharacter = 0x0001_0004,
    ChannelScriptVersion = 0x05,
    ChannelScript = 0x09,
    ChannelConnect = 0x0B,
    ChannelInfo = 0x11,
    GameExit = 0x0001_0003,
    GameReturnToSelection = 0x0001_0007,
    GameClientSpecStatistic = 0x0001_00AB,
    GameClientHardwareDetail = 0x0001_01DD,
    GameClientSpecProfile = 0x0001_00C2,
    GameSessionMetrics = 0x0001_00FA,
    GameAcceptQuest = 0x0001_001F,
    GameGiveUpQuest = 0x0001_0020,
    GameSetQuestTrigger = 0x0001_0021,
    GameFinishQuest = 0x0001_0022,

    GameTaggedIdReport = 0x0001_008F,
    GameRosterFollowUp = 0x0001_02A8,
    GameCheckCharacterName = 0x0001_02B5,
    GameCreateCharacter = 0x0001_0005,
    GameDeleteCharacter = 0x0001_0006,
    GameMercenaryInfo = 0x0001_01BA,
    GameHiddenCharacterInfo = 0x0001_0286,
    GameSaveCharacterOption = 0x0001_01C0,
    GameSaveGameOption1 = 0x0001_00C5,
    GameContentsPlayInfo = 0x0001_02C1,

    // The client's own periodic liveness probe on the Game endpoint. It arrives with no body
    // and keeps coming for the life of the connection whether or not it is answered.
    // See Game/ConnectionCheck.
    GameConnectionCheck = 0x0001_04DD,

    // Native town-party lifecycle commands. REQUEST/RESPONSE type zero is the party branch;
    // other peer-request types remain deliberately outside this implementation.
    GameRequestPeer = 0x0001_000A,
    GameResponsePeer = 0x0001_000B,
    GameLeaveParty = 0x0001_000D,
    GameRemovePartyMember = 0x0001_000E,
    GameChangePartyLeader = 0x0001_0079,
    GameCallPartyMemberRealtimeInfo = 0x0001_00A6,
    GameSetPartyInfo = 0x0001_000C,

    // SET_UDP_IP_PORT is sent with the map-entry reports, but it supplies the party endpoint and
    // therefore belongs to the party slice rather than the fire-and-forget report family.
    GameSetUdpIpPort = 0x0001_0002,

    // The remaining map-entry report family: sent once the entry batch is accepted and the client
    // starts loading a town. None is answered. See Game/MapEntryReports.
    GameCeraQuery = 0x0001_003F,
    // Persistent seen-notice sets, independent of the map-entry telemetry family.
    GameNoticeSeen = 0x0001_01DE,
    GameInformNoticeSecond = 0x0001_01F8,
    GameSecurityStatus = 0x0001_0252,
    GameRuntimeTingStatistic = 0x0001_02B3,
    GameEnterSelectDungeon = 0x0001_000F,

    // Committing to a dungeon and asking to be put in its first room.
    // See Game/DungeonEntry.
    GameSelectDungeon = 0x0001_0010,

    // The gate at the end of the loading bar. See Game/FinishLoading.
    GameFinishLoading = 0x0001_0025,

    // Walking through a door into an adjoining room. See Game/DungeonEntry.
    GameMoveMap = 0x0001_002D,

    // The client reporting a room actor dead. See Game/MonsterDeath.
    GameMonsterDeath = 0x0001_0027,
    GameCharacterDeath = 0x0001_0028,
    GameUseRevivalCoin = 0x0001_0029,

    // Picking a dropped item up off a room's floor. See Game/GroundPickup.
    GameGetItem = 0x0001_002B,

    // The client reporting how the run went, once the exit has opened. See Game/DungeonClear.
    GameSetPlayResult = 0x0001_002E,

    // The client's own word that the boss is down, which nothing answers. See Game/DungeonClear.
    GameBossDieCheck = 0x0001_0075,

    // The result scroll has finished drawing, and the run waits on the answer. See
    // Game/DungeonClear.
    GameScoreScrollState = 0x0001_0045,

    // Asking for the card panel's layout, and turning one of its cards over.
    // See Game/CardReward.
    GameCardSelectRightState = 0x0001_0046,
    GameSelectCard = 0x0001_0047,

    // The button that walks a character out of a finished run. See Game/DungeonClear.
    GameEplpCommand = 0x0001_0048,

    // The tutorial asking for the level it promised. See Game/TutorialLevelUp.
    GameTutorialLevelUp = 0x0001_01E4,

    // The reports the reference server answers with nothing. One handler, six wire keys, and the
    // silence is a decision rather than a gap. See Game/UnansweredReports.
    GameUseSkill = 0x0001_0026,
    GameSyncItemSpace = 0x0001_035C,
    GameDecreaseDurability = 0x0001_0030,
    GameOverflowInfo = 0x0001_00D9,
    GameLogDomainConnect = 0x0001_04F1,
    GameLogConnectProcess = 0x0001_04D7,
    GameCharacterStatistic = 0x0001_007B,
    GameRequestBlacklist = 0x0001_0078,
    GameFarmEventAction = 0x0001_02BE,
    GamePi = 0x0001_011B,
    GameRequestIgaVersion = 0x0001_039D,
    GameFpsPerformanceStatistic = 0x0001_02EF,
    GameFrameLagPerDungeon = 0x0001_045C,
    GameLagStatistics = 0x0001_0117,

    // Leaving the dungeon, which is the only thing that ends a run. See Game/DungeonClear.
    GameGiveUpGame = 0x0001_002A,

    // Closing the dungeon-selection screen. See Game/DungeonSelection.
    GameBackToVillage = 0x0001_0084,

    // Consuming one item off a pile. See Game/UseStackable.
    GameUseStackable = 0x0001_002C,
    GameRenameCreature = 0x0001_0064,
    GameCreatureResponseReport = 0x0001_0065,
    GameCreatureScriptMessage = 0x0001_007A,
    GameQuestClearCoupon = 0x0001_0204,

    // An item changing slots, the quick-slot bar included. See Game/MoveItemSpace.
    GameMoveItemSpace = 0x0001_0013,
    GameSortItem = 0x0001_0014,
    GameSortLock = 0x0001_02CA,
    GameSortUnlock = 0x0001_02CB,

    // Pausing and resuming a scripted moment inside a dungeon. See Game/StoryPause.
    GameStoryPause = 0x0001_00BF,

    // Where the character is standing, and where it walks to. See Game/TownProjection.
    GameSetUserPosition = 0x0001_0023,
    GameSetUserArea = 0x0001_0024,
}

public enum ServerMessageType
{
    GameMailboxSend = 0x0001_005E,
    GameMailboxSendMulti = 0x0001_013B,
    GameMailboxClaim = 0x0001_005F,
    GameMailboxAction = 0x0001_0086,
    GameMailboxOpen = 0x0001_0060,
    GameMailboxQuery = 0x0001_0144,
    GameMailboxAlarm = 0x0000_0063,
    GameMagicBoxBatchOpening = 0x0001_0450,
    GameMagicBoxSingleOpening = 0x0001_00D0,
    GameVendingMachine = 0x0001_00D7,
    GameLotteryOpening = 0x0001_001B,
    GamePackageOpening = 0x0001_00A0,
    GameAvatarPackageOpening = 0x0001_0207,
    GameGenCeraTicket = 0x0001_0041,
    GameServerNotice = 0x0000_01F7,
    GameCeraShopPurchase = 0x0001_0040,
    GameBuyItem = 0x0001_0015,
    GameSellItem = 0x0001_0016,
    GameRepairItem = 0x0001_0017,
    GameRecoverStamina = 0x0001_0009,
    GameDisjointItem = 0x0001_001A,
    GameReinforceItem = 0x0001_0050,
    GameDecreaseDurability = 0x0001_0030,
    GameDeleteItem = 0x0001_0012,
    GameCrystalContract = 0x0001_0218,
    GameLearnSkill = 0x0001_001D,
    GameChangeSkillSlot = 0x0001_001C,
    GameSwitchSkillBuild = 0x0001_0104,
    GameShopPurchaseCount = 0x0001_02CC,
    GameInitialConnection = 0x0000_0001,
    GameLogin = 0x0001_0001,
    GameAccountCharacterRoster = 0x0000_0002,
    // Synthetic catalogue identity for USERINFO subtype 3. The encoder emits NOTI 0x0002, but a
    // distinct message type keeps the current-user and inspected-user projections type-safe.
    GameOtherCharacterDetail = 0x0000_0302,
    GameStateSynchronization = 0x7FFF_0001,
    ChannelScriptVersion = 0x06,
    ChannelScript = 0x0A,
    ChannelConnect = 0x0C,
    ChannelInfo = 0x12,
    GameExit = 0x0001_0003,
    GameReturnToSelection = 0x0001_0007,
    GameRemovePartyMemberRefusal = 0x0001_000E,
    GameCheckCharacterName = 0x0001_02B5,
    GameCreateCharacter = 0x0001_0005,
    GameDeleteCharacter = 0x0001_0006,
    GameMercenaryInfo = 0x0001_01BA,
    GameHiddenCharacterInfo = 0x0001_0286,
    GameHonorLevelInfo = 0x0000_0289,
    GameAuctionServiceState = 0x0000_00B7,
    GameInformNotice = 0x0000_01A1,
    GameRosterFollowUp = 0x0001_02A8,
    GameContentsPlayInfo = 0x0001_02C1,
    GameConnectionCheck = 0x0001_04DD,
    GameSecurityStatusAcknowledgement = 0x0001_0252,
    GameEnterSelectDungeonAcknowledgement = 0x0001_000F,

    // Native town-party NOTI payloads. These keys are type zero because they are server push
    // notifications rather than direct replies to the C2S command keys.
    GamePartyInviteRequest = 0x0000_0007,
    GamePartyInviteAccepted = 0x0000_0008,
    GamePartyRoster = 0x0000_0009,
    // Synthetic catalogue identity: the encoder emits NOTI 0x0009 type 1, while a distinct key
    // keeps settings-only projection separate from the type-zero/type-three roster variants.
    GamePartySettings = 0x7FFF_0009,
    GamePartyEndpoints = 0x0000_000B,
    GamePartyRealtime = 0x0000_0099,

    // Pushed on every area change. See Game/TownProjection.
    GameAreaUsers = 0x0000_0018,

    // Pushed when the dungeon-selection screen opens. See Game/DungeonSelection.
    GameUserState = 0x0000_0003,
    GameUserArea = 0x0000_0017,
    GameUserPosition = 0x0000_0016,
    GameUdpHost = 0x0000_001A,
    GameEnterSelectDungeonState = 0x0000_001B,

    // The dungeon-entry answer: the run's shape, the room it opens in, and the support
    // roster. See Game/DungeonEntry.
    GameDungeonInfo = 0x0000_001C,
    GameStartMap = 0x0000_001D,
    GameSupportTag = 0x0000_019F,

    // The acknowledgement and the notification that release a loading screen.
    // See Game/FinishLoading.
    GameFinishLoading = 0x0001_0025,
    GameLoadingComplete = 0x0000_001E,

    // Acknowledges a tutorial flag change and carries what the step gave.
    // See Game/TaggedIdReport.
    GameAcceptQuest = 0x0001_001F,
    GameGiveUpQuest = 0x0001_0020,
    GameSetQuestTrigger = 0x0001_0021,
    GameFinishQuest = 0x0001_0022,

    GameTaggedIdReport = 0x0001_008F,

    // Whether the item was consumed. See Game/UseStackable.
    GameUseStackable = 0x0001_002C,
    GameRenameCreature = 0x0001_0064,
    GameQuestClearCoupon = 0x0001_0204,

    GameCreatureDeath = 0x0000_0064,
    GameCreatureRename = 0x0000_0065,
    GameCreatureExperience = 0x0000_0066,
    GameCreatureState = 0x0000_0067,
    GameCreatureResponse = 0x0000_0068,
    GameCreatureEvolution = 0x0000_006A,
    GameCreatureRevival = 0x0000_006B,

    // Where a moved item ended up, or why it did not move. See Game/MoveItemSpace.
    GameMoveItemSpace = 0x0001_0013,
    GameSortItem = 0x0001_0014,
    GameSortLock = 0x0001_02CA,
    GameSortUnlock = 0x0001_02CB,

    // Confirms a scripted pause. Answers CMD 0x00BF under a different type.
    // See Game/StoryPause.
    GameStoryPauseState = 0x0000_00AA,

    // What the kill was worth, and the kill acknowledged. See Game/MonsterDeath.
    GameExperience = 0x0000_0025,
    GameMonsterDeath = 0x0000_0026,
    GameCharacterLifeState = 0x0000_0020,
    GameFatigue = 0x0000_0024,
    GameWeaknessProjection = 0x0000_0021,
    GameCharacterDeath = 0x0001_0028,
    GameUseRevivalCoin = 0x0001_0029,

    // Whether the item was picked up, and where it landed. See Game/GroundPickup.
    GameGetItem = 0x0001_002B,
    GameGroundPickup = 0x0000_0027,

    // Whether the tutorial's level was granted. See Game/TutorialLevelUp.
    GameTutorialLevelUp = 0x0001_01E4,

    // The exit opening, and the result screen behind it. See Game/DungeonClear.
    GameEnableClearDungeon = 0x0000_001F,
    GameSecretShopNpc = 0x0000_0117,
    GamePlayResult = 0x0000_0022,
    GameClearDungeonReward = 0x0000_0023,
    GameScoreScrollState = 0x0001_0045,
    GameEplpCommand = 0x0001_0048,
    GameGiveUpGame = 0x0001_002A,

    // The card panel's layout, and what every one of its positions turned out to hold.
    // See Game/CardReward.
    GameCardSelectRightState = 0x0001_0046,
    GameSelectCard = 0x0001_0047,

    // Why the character is not going into the dungeon they picked. See Game/DungeonEntry.
    GameSelectDungeon = 0x0001_0010,

    // The selection screen closed. See Game/DungeonSelection.
    GameBackToVillage = 0x0001_0084,
}

public interface IClientNetworkMessage
{
    ClientMessageType MessageType { get; }
}

public interface IServerNetworkMessage
{
    ServerMessageType MessageType { get; }
}

public sealed record ChannelMessageMetadata(
    byte Classification,
    byte MessageNumber,
    uint Checksum,
    byte Acknowledgement)
{
    public static ChannelMessageMetadata Empty { get; } = new(0, 0, 0, 0);
}

public sealed record GameMessageMetadata(
    byte Selector,
    ushort Type,
    uint Checksum,
    ushort Sequence)
{
    public static GameMessageMetadata Empty { get; } = new(0, 0, 0, 0);
}
