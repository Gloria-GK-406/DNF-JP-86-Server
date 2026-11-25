using System.Collections.Immutable;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.PvpMatchmaking;

public sealed record JoinPvpMatchmakingRequest(GameMessageMetadata Metadata, PvpBattleMode? Mode,
    ImmutableArray<CharacterCareer> ExcludedCareers)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameJoinPvpMatchmaking;
    public bool Colosseum { get; init; }
}

public sealed record CancelPvpMatchmakingRequest(GameMessageMetadata Metadata, PvpBattleMode? Mode) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameCancelPvpMatchmaking;
}

public sealed record ConfirmPvpMatchmakingRequest(GameMessageMetadata Metadata, uint? RoomNumber) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameConfirmPvpMatchmaking;
}

public sealed record PvpMatchmakingCountRequest(GameMessageMetadata Metadata, bool WellFormed) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GamePvpMatchmakingCount;
}

internal sealed record JoinPvpMatchmakingResponse(PvpBattleMode? AcceptedMode, bool Colosseum = false) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameJoinPvpMatchmakingResponse;
}

internal sealed record CancelPvpMatchmakingResponse(bool Accepted) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCancelPvpMatchmakingResponse;
}

internal sealed record PvpMatchmakingCountNotice(PvpMatchmakingCounts Counts) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpMatchmakingCountNotice;
}

internal sealed record PvpMatchmakingOpponent(ushort UserId, byte Side, byte Grade, int OfficialPoints);

internal sealed record PvpMatchmakingRoster(ushort RoomNumber, IReadOnlyList<PvpMatchmakingOpponent> Opponents) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpMatchmakingRoster;
}

internal sealed record PvpMatchmakingStateNotice(byte State) : IGameNotiMessage
{
    public static PvpMatchmakingStateNotice Cancelled { get; } = new(1);
    public static PvpMatchmakingStateNotice Preload { get; } = new(2);

    public ServerMessageType MessageType => ServerMessageType.GamePvpMatchmakingState;
}

internal sealed record PvpActorLoaded(ushort UserId) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpActorLoaded;
}

internal sealed record PvpActorsConnected : IGameNotiMessage
{
    public static PvpActorsConnected Instance { get; } = new();
    public ServerMessageType MessageType => ServerMessageType.GamePvpActorsConnected;
}

internal sealed record PvpMatchmakingComplete : IGameNotiMessage
{
    public static PvpMatchmakingComplete Instance { get; } = new();
    public ServerMessageType MessageType => ServerMessageType.GamePvpMatchmakingComplete;
}
