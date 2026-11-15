using System.Collections.Immutable;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.PvpBattle;

/// <summary>CMD <c>0x012A</c> <c>COMPLETE_LOAD_PVP</c>: empty; the fighter finished loading the battle. Only logged (spec SA-1).</summary>
/// <param name="Length">The body's length; anything but 0 is not a report.</param>
public sealed record CompleteLoadPvpRequest(GameMessageMetadata Metadata, int Length) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameCompleteLoadPvp;
}

/// <summary>CMD <c>0x012B</c> <c>CONNECT_P2P_PVP</c>: <c>u8 n</c> then <c>n × (u8 seat, u8 status)</c>. Only logged (spec SA-1).</summary>
/// <param name="Peers">The seats the client reports and each one's link status; only logged.</param>
/// <param name="WellFormed">False for a body that is not exactly <c>1 + 2n</c> bytes with n at most 8 and every seat in range.</param>
public sealed record ConnectP2pPvpRequest(
    GameMessageMetadata Metadata, ImmutableArray<(byte Seat, byte Status)> Peers, bool WellFormed, int Length) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameConnectP2pPvp;
}

/// <summary>CMD <c>0x006E</c> <c>PVP_HEART_BEAT</c>: empty.</summary>
public sealed record PvpHeartbeatRequest(GameMessageMetadata Metadata, int Length) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GamePvpHeartbeat;
}

/// <summary>
/// CMD <c>0x0037</c> <c>DIE_PVP_CHARACTER</c>: <c>u16 victim user id, u16 killer user id, u8 flag</c>, exactly five
/// bytes (Spec RQ-075).
/// </summary>
/// <param name="Flag">Upstream: an actor-property comparison, not a kill permission; only logged.</param>
/// <param name="WellFormed">False for any other length.</param>
public sealed record DiePvpCharacterRequest(
    GameMessageMetadata Metadata, ushort Victim, ushort Killer, byte Flag, bool WellFormed, int Length) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameDiePvpCharacter;
}

/// <summary>CMD <c>0x0038</c> <c>PVP_TIME_OUT</c>: 32 bytes (eight <c>i32</c>), handed to the room as they are.</summary>
/// <param name="WellFormed">False for any length other than 32 (Spec RQ-075).</param>
public sealed record PvpTimeOutRequest(GameMessageMetadata Metadata, ImmutableArray<byte> Values, bool WellFormed) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GamePvpTimeOut;
}

/// <summary>CMD <c>0x003A</c> <c>RES_PVP_RANK</c>: the client's ranking block, taken only as the acknowledgement it is.</summary>
public sealed record PvpRankResponseRequest(GameMessageMetadata Metadata, int Length) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GamePvpRankResponse;
}

/// <summary>CMD <c>0x0039</c> <c>END_PVP_RESULT</c>: empty; the fighter closed the result screen.</summary>
public sealed record EndPvpResultRequest(GameMessageMetadata Metadata, int Length) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameEndPvpResult;
}

/// <summary>NOTI <c>0x002E</c> <c>DIE_PVP_CHARACTER</c>: <c>u8 victim seat, u8 killer seat</c>, 0xFF for nobody.</summary>
public sealed record PvpDeathMessage(int VictimSeat, int? KillerSeat) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameDiePvpCharacter;
}

/// <summary>NOTI <c>0x0031</c> <c>REQ_PVP_RANK</c>: empty; the battle is over and the ranking opens.</summary>
public sealed record PvpRankRequestMessage : IGameNotiMessage
{
    public static PvpRankRequestMessage Instance { get; } = new();

    public ServerMessageType MessageType => ServerMessageType.GameRequestPvpRank;
}

/// <summary>One fighter of the frozen battle roster, as <c>0x002F</c> lists them.</summary>
public sealed record EndPvpFighter(ushort WireUserId, int Kills, byte Grade, long Experience);

/// <summary>NOTI <c>0x002F</c> <c>END_PVP</c>: one recipient's result screen; <c>26 + 19n</c> bytes.</summary>
/// <param name="WinnerSeat">The winning seat, for a team the first standing seat of the winning team; 0xFF for a draw.</param>
/// <param name="Grade">The recipient's own grade after the battle.</param>
/// <param name="ExperienceChange">The recipient's own grade experience change.</param>
/// <param name="Fighters">Every fighter the battle started with, in seat order, departed ones included.</param>
public sealed record EndPvpMessage(
    byte WinnerSeat, byte Grade, long ExperienceChange, IReadOnlyList<EndPvpFighter> Fighters, bool Draw) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEndPvp;
}

/// <summary>NOTI <c>0x0030</c> <c>PVP_RECORD</c>: the recipient's duel record, 51 bytes.</summary>
public sealed record PvpRecordMessage(PvpRecordView Record) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpRecord;
}
