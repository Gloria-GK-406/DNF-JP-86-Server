using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;

namespace Dfo.Server.Abstractions.Pvp;

/// <summary>
/// A duel room the lobby knows about.
/// </summary>
/// <param name="Published">
/// Whether the room has finished being announced. A room still mid-announcement is not
/// shown, because a character who joined it now would race the announcement.
/// </param>
public sealed record PvpRoom(
    ushort RoomId,
    CharacterId OwnerCharacterId,
    int ListenerPort,
    byte NameType,
    short MapIndex,
    bool HasPassword,
    byte BattleMode,
    bool Published,
    byte[]? Name = null,
    byte RoomState = 0,
    byte ManagerSeat = 0,
    IReadOnlyList<PvpRoomSeatState>? Seats = null,
    int MatchingType = 0);

/// <summary>The duel rooms on offer, as everyone else reads them.</summary>
public interface IPvpRoomQueryDomainService
{
    /// <summary>The rooms on the same listener that have finished being announced.</summary>
    IReadOnlyList<PvpRoom> OfferedOn(int listenerPort);
}

/// <summary>The single owner of duel rooms.</summary>
public interface IPvpRoomDomainService : IPvpRoomQueryDomainService
{
    /// <summary>Records a room, replacing any earlier record with the same identifier.</summary>
    void Offer(PvpRoom room);

    /// <summary>Removes a room. Returns false when it was not recorded.</summary>
    bool Withdraw(ushort roomId);
}
