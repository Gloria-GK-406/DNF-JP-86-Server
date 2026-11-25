using Dfo.Server.Data.Game;

namespace Dfo.Server.Abstractions.Pvp;

/// <summary>A committed room as the lobby shows it; the one mapping both the entry snapshot and the lobby's increments use.</summary>
/// <remarks>The wire bytes are the protocol codec's; this only names what each seat and the room are.</remarks>
public static class PvpRoomLobbyView
{
    public static PvpRoomSnapshotState Of(PvpRoomSnapshot room)
    {
        ArgumentNullException.ThrowIfNull(room);
        return new(
            room.Number,
            room.NameType,
            [.. room.Name],
            room.Phase == PvpRoomPhase.Waiting,
            checked((byte)room.OwnerSeat),
            checked((short)room.MapIndex),
            (byte)room.Mode,
            [.. room.Seats.Select(Seat)],
            room.HasPassword);
    }

    public static PvpRoomSeatState Seat(PvpSeat seat) => seat switch
    {
        PvpSeat.Taken taken => new(PvpRoomSeatOccupancy.Taken,
            taken.Observation is { } observation ? (byte)observation : (byte)taken.Team, taken.Session.WireUserId, taken.Ready),
        PvpSeat.Closed => new(PvpRoomSeatOccupancy.Closed, 0, 0, false),
        PvpSeat.Open => new(PvpRoomSeatOccupancy.Open, 0, 0, false),
        _ => throw new ArgumentOutOfRangeException(nameof(seat)),
    };
}
