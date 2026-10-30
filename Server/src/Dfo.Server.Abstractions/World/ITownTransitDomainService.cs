using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>A character to stand in its town location on a channel.</summary>
public sealed record TownReturnRequest(
    SelectedSessionIdentity Session, int ChannelId, CharacterPositionState Position);

/// <param name="Previous">Where the character stood in town before, or null when it was away.</param>
public sealed record TownReturnAdmission(
    TownReturnRequest Request, TownAreaMembershipChange Change, TownAreaMember? Previous);

public sealed record TownDepartureResult(
    SelectedSessionIdentity Session,
    TownAreaMembershipChange Change,
    TownAreaKey PreviousArea,
    bool SharedTownPresence);

/// <summary>Why the couple-room rules refuse an area crossing.</summary>
public enum TownCrossingRefusal
{
    /// <summary>Only a married character enters, from an entry source or from inside, to area 0.</summary>
    CoupleRoomEntry,

    /// <summary>The couple room is left only for the area it was entered from.</summary>
    CoupleRoomExit,
}

/// <summary>
/// Moves characters being played between town areas, out of town and back into it.
/// </summary>
/// <remarks>
/// The runtime record keeps where a character stands; this service decides which area a move lands
/// in -- the couple room is one instance per household -- and what crossing it starts and ends:
/// walking into the couple room remembers where to return to, and walking out closes the session's
/// housing lease and that remembered return. Callers project the results; none of these writes
/// belong to a projection.
/// </remarks>
public interface ITownTransitDomainService
{
    /// <summary>
    /// Whether the couple-room rules refuse a character standing at <paramref name="standing"/>
    /// crossing into this area; null when they allow it or do not apply.
    /// </summary>
    ValueTask<TownCrossingRefusal?> RefuseCrossingAsync(
        SelectedSessionIdentity session,
        CharacterPositionState standing,
        byte townId,
        byte areaId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The position to store for a crossing from <paramref name="standing"/> to
    /// <paramref name="destination"/>. A couple-room position is never stored: a character inside
    /// keeps the place it entered from, so the next login lands there.
    /// </summary>
    CharacterPositionState StoredPositionAfter(CharacterPositionState standing, CharacterPositionState destination);

    /// <summary>Crosses a character standing in town into another area of the channel.</summary>
    /// <returns>Null when the character is not standing in town, has no couple room to enter, or nothing changed.</returns>
    ValueTask<TownAreaMembershipChange?> CrossAsync(
        SelectedSessionIdentity session,
        int channelId,
        byte townId,
        byte areaId,
        short x,
        short y,
        bool sharesTownPresence,
        CancellationToken cancellationToken);

    /// <summary>Crosses into an area whose key is already resolved.</summary>
    /// <param name="direction">The facing to take; the current one when null.</param>
    TownAreaMembershipChange? Cross(
        SelectedSessionIdentity session,
        TownAreaKey destination,
        short x,
        short y,
        bool sharesTownPresence,
        byte? direction = null);

    /// <summary>
    /// Stands each character back in town. Sessions no longer in the world, and couple-room
    /// locations of characters without a household, are skipped.
    /// </summary>
    /// <returns>The admissions, in request order.</returns>
    ValueTask<IReadOnlyList<TownReturnAdmission>> ReturnToTownAsync(
        IReadOnlyList<TownReturnRequest> requests, CancellationToken cancellationToken);

    /// <summary>Takes each character standing in town out of it, keeping where it stood.</summary>
    IReadOnlyList<TownDepartureResult> LeaveTown(
        IReadOnlyList<SelectedSessionIdentity> sessions, TownAreaDepartureReason reason);
}
