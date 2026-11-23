using System.Collections.Immutable;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.PvpRoom;

/// <summary>What CMD <c>0x0032</c> asks for, once its body has been read whole.</summary>
/// <param name="Name">The room name when <paramref name="NameType"/> is 0; empty otherwise.</param>
/// <param name="Password">The password offered, or null when the password flag was 0.</param>
/// <param name="Special">Upstream: 1 selects the practice room (battle mode 6); anything else a normal room.</param>
public sealed record MakePvpRoomInput(
    byte NameType, ImmutableArray<byte> Name, short MapIndex, ImmutableArray<byte>? Password, byte Special);

/// <summary>CMD <c>0x0032</c> <c>MAKE_PVP_ROOM</c>.</summary>
/// <remarks><paramref name="Input"/> is null for a body that is not exactly one request; it is still answered.</remarks>
public sealed record MakePvpRoomRequest(GameMessageMetadata Metadata, MakePvpRoomInput? Input) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameMakePvpRoom;
}

/// <summary>CMD <c>0x0033</c> <c>ENTER_PVP_ROOM</c>: a room by its number, with the password offered for it.</summary>
/// <param name="Password">Null when the password flag was 0.</param>
/// <param name="WellFormed">False for a body that is not exactly one request.</param>
public sealed record EnterPvpRoomRequest(
    GameMessageMetadata Metadata, ushort Number, ImmutableArray<byte>? Password, bool WellFormed) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameEnterPvpRoom;
}

/// <summary>CMD <c>0x0062</c>, quick join from the duel lobby.</summary>
public sealed record QuickJoinPvpRoomRequest(GameMessageMetadata Metadata, bool WellFormed) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameQuickJoinPvpRoom;
}

/// <summary>CMD <c>0x0034</c> <c>SET_PVP_SEAT_STATE</c>: <c>u8 seat, u8 state</c>.</summary>
public sealed record SetPvpSeatStateRequest(GameMessageMetadata Metadata, byte Seat, byte State, bool WellFormed) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetPvpSeatState;
}

/// <summary>CMD <c>0x0035</c> <c>SET_PVP_READY_STATE</c>: <c>u8 ready</c>, any non-zero value being ready.</summary>
public sealed record SetPvpReadyStateRequest(GameMessageMetadata Metadata, bool Ready, bool WellFormed) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetPvpReadyState;
}

/// <summary>CMD <c>0x0036</c> <c>SET_PVP_TEAM_MODE</c>: <c>u8 mode</c> and nothing else (Spec RQ-075).</summary>
public sealed record SetPvpTeamModeRequest(GameMessageMetadata Metadata, byte Mode, bool WellFormed) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetPvpTeamMode;
}

/// <summary>CMD <c>0x003B</c> <c>SET_PVP_MAP_INDEX</c>: <c>i16</c> index into the archive's duel map order, 0 random.</summary>
public sealed record SetPvpMapIndexRequest(GameMessageMetadata Metadata, short Index, bool WellFormed) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetPvpMapIndex;
}

/// <summary>A refused room command: <c>00 reason</c> on the command's own number.</summary>
/// <remarks>The reason codes and where each comes from are in <see cref="PvpRoomRefusalCode"/>.</remarks>
public sealed record PvpRoomCommandRefusal : IGameCmdMessage
{
    private PvpRoomCommandRefusal(ServerMessageType type, byte reason)
    {
        MessageType = type;
        Reason = reason;
    }

    public ServerMessageType MessageType { get; }

    public byte Reason { get; }

    public static PvpRoomCommandRefusal MakeRoom(byte reason) => new(ServerMessageType.GameMakePvpRoom, reason);
    public static PvpRoomCommandRefusal EnterRoom(byte reason) => new(ServerMessageType.GameEnterPvpRoom, reason);
    public static PvpRoomCommandRefusal QuickJoinRoom(byte reason) => new(ServerMessageType.GameQuickJoinPvpRoom,
        reason == PvpRoomRefusalCode.RoomGoneOrNotReady
            ? PvpRoomRefusalCode.NoSuitableRoom : PvpRoomRefusalCode.QuickJoinUnavailable);
    public static PvpRoomCommandRefusal SeatState(byte reason) => new(ServerMessageType.GameSetPvpSeatState, reason);
    public static PvpRoomCommandRefusal ReadyState(byte reason) => new(ServerMessageType.GameSetPvpReadyState, reason);
    public static PvpRoomCommandRefusal TeamMode(byte reason) => new(ServerMessageType.GameSetPvpTeamMode, reason);
    public static PvpRoomCommandRefusal MapIndex(byte reason) => new(ServerMessageType.GameSetPvpMapIndex, reason);
}

/// <summary>CMD <c>0x0033</c> success: <c>01</c> and every seat's ready flag in seat order.</summary>
/// <remarks>Upstream <c>BuildEnterSuccessBody</c>; an entry through an accepted invitation has no command reply.</remarks>
public sealed record EnterPvpRoomAccepted(ImmutableArray<bool> Ready) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEnterPvpRoom;
}

public sealed record QuickJoinPvpRoomAccepted : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameQuickJoinPvpRoom;
}

/// <summary>NOTI <c>0x0029</c> <c>PVP_ROOM_INFO</c>: these rooms as the lobby shows them, added or replaced.</summary>
public sealed record PvpRoomInfoMessage(IReadOnlyList<PvpRoomSnapshotState> Rooms) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpRoomInfo;
}

/// <summary>NOTI <c>0x002A</c> <c>PVP_ROOM_STATE</c>: a room's state, owner, map and mode; state 0 when it is gone.</summary>
public sealed record PvpRoomStateMessage(ushort Number, byte State, byte OwnerSeat, short MapIndex, byte Mode) : IGameNotiMessage
{
    /// <summary>Upstream <c>BuildDestroyedRoomStateBody</c>: state 0, owner 0, map 0, the normal mode 2.</summary>
    public static PvpRoomStateMessage Destroyed(ushort number) => new(number, 0, 0, 0, 2);

    public static PvpRoomStateMessage Of(PvpRoomSnapshotState room) => new(
        room.RoomNumber,
        StateSynchronization.SupplementalSynchronizationCodec.PvpRoomStateByte(room.Waiting),
        room.OwnerSeat,
        room.MapIndex,
        room.BattleMode);

    public ServerMessageType MessageType => ServerMessageType.GamePvpRoomState;
}

/// <summary>NOTI <c>0x002B</c> <c>PVP_SEAT_STATE</c>: these seats of a room as they now stand, with the room's mode.</summary>
public sealed record PvpSeatStateMessage(
    ushort Number, byte Mode, IReadOnlyList<(int Seat, PvpRoomSeatState State)> Seats) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpSeatState;
}

/// <summary>NOTI <c>0x002C</c> <c>PVP_READY_STATE</c>: <c>u8 seat, u8 ready</c>.</summary>
public sealed record PvpReadyStateMessage(int Seat, bool Ready) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpReadyState;
}

/// <summary>NOTI <c>0x002D</c> <c>START_PVP</c>: <c>u8 map, u8 mode</c>; the map is the one drawn, never the random choice.</summary>
public sealed record StartPvpMessage(byte Map, byte Mode) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameStartPvp;
}
