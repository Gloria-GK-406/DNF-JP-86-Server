namespace Dfo.Server.Data.Networking;

public enum ClientMessageType
{
    GameLogin = 0x0001_0001,
    GameGetAccountCharacterRoster = 0x0001_0208,
    GameSelectCharacter = 0x0001_0004,
    ChannelScriptVersion = 0x05,
    ChannelScript = 0x09,
    ChannelConnect = 0x0B,
    ChannelInfo = 0x11,
    GameExit = 0x0001_0003,
    GameClientSpecStatistic = 0x0001_00AB,
    GameClientHardwareDetail = 0x0001_01DD,
    GameClientSpecProfile = 0x0001_00C2,
    GameSessionMetrics = 0x0001_00FA,
    GameTaggedIdReport = 0x0001_008F,
    GameRosterFollowUp = 0x0001_02A8,
    GameCheckCharacterName = 0x0001_02B5,
    GameCreateCharacter = 0x0001_0005,
    GameDeleteCharacter = 0x0001_0006,
    GameMercenaryInfo = 0x0001_01BA,
    GameHiddenCharacterInfo = 0x0001_0286,
    GameSaveGameOption1 = 0x0001_00C5,
    GameContentsPlayInfo = 0x0001_02C1,

    // The map-entry report family: sent once the entry batch is accepted and the client starts
    // loading a town. None is answered. See Game/MapEntryReports.
    GameSetUdpIpPort = 0x0001_0002,
    GameCeraQuery = 0x0001_003F,
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

    // Where the character is standing, and where it walks to. See Game/TownProjection.
    GameSetUserPosition = 0x0001_0023,
    GameSetUserArea = 0x0001_0024,
}

public enum ServerMessageType
{
    GameInitialConnection = 0x0000_0001,
    GameLogin = 0x0001_0001,
    GameAccountCharacterRoster = 0x0000_0002,
    GameStateSynchronization = 0x7FFF_0001,
    ChannelScriptVersion = 0x06,
    ChannelScript = 0x0A,
    ChannelConnect = 0x0C,
    ChannelInfo = 0x12,
    GameExit = 0x0001_0003,
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

    // Pushed on every area change. See Game/TownProjection.
    GameAreaUsers = 0x0000_0018,

    // Pushed when the dungeon-selection screen opens. See Game/DungeonSelection.
    GameUserState = 0x0000_0003,
    GameUserArea = 0x0000_0017,
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
