using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Game;

namespace Dfo.Server.Application.Pvp;

/// <summary>A committed room as the entry state carries it to the lobby snapshot.</summary>
internal static class PvpRoomLobbyView
{
    public static PvpRoomSnapshotState Of(PvpRoomSnapshot room) => new(
        room.Number,
        room.NameType,
        [.. room.Name],
        room.Phase == PvpRoomPhase.Waiting,
        checked((byte)room.OwnerSeat),
        checked((short)room.MapIndex),
        (byte)room.Mode,
        [.. room.Seats.Select(Seat)],
        room.HasPassword);

    private static PvpRoomSeatState Seat(PvpSeat seat) => seat switch
    {
        PvpSeat.Taken taken => new(PvpRoomSeatOccupancy.Taken, (byte)taken.Team, taken.Session.WireUserId, taken.Ready),
        PvpSeat.Closed => new(PvpRoomSeatOccupancy.Closed, 0, 0, false),
        PvpSeat.Open => new(PvpRoomSeatOccupancy.Open, 0, 0, false),
        _ => throw new ArgumentOutOfRangeException(nameof(seat)),
    };
}
