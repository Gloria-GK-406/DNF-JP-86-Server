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

/// <summary>
/// Moves characters being played between town areas, out of town and back into it.
/// </summary>
/// <remarks>
/// The runtime record keeps where a character stands; this service decides which area a move lands
/// in -- the couple room is one instance per household -- and what leaving an area ends: walking
/// out of the couple room closes the session's housing lease and its remembered return. Callers
/// project the results; none of these writes belong to a projection.
/// </remarks>
public interface ITownTransitDomainService
{
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
