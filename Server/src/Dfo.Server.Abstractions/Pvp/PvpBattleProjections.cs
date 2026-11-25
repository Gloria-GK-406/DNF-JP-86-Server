using System.Collections.Immutable;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Pvp;

/// <summary>One committed room departure waiting for its projection.</summary>
public readonly record struct PvpDepartureEntryId(Guid Value);

/// <summary>A committed room departure and the settlement it led to, as selected-session cleanup handed it over.</summary>
public sealed record PvpCommittedDeparture(PvpRoomOutcome.Changed Departure, PvpSettlementOutcome? Settled);

/// <summary>
/// The committed room departures awaiting projection: selected-session cleanup enqueues each one through
/// <see cref="IPvpRoomDepartureSink"/>, and the duel deadline round claims them in order.
/// </summary>
public interface IPvpDepartureProjectionDomainService
{
    ImmutableArray<PvpDepartureEntryId> SnapshotPending();

    /// <summary>Takes the head entry, only when it is the one expected.</summary>
    bool TryClaim(PvpDepartureEntryId expected, out PvpCommittedDeparture? departure);
}

/// <summary>
/// The room members who have not been shown seats emptied while their battle ran (Spec RQ-035): a client keeps its
/// battle actors until it leaves the result screen, so the seats stay shown taken until then.
/// </summary>
/// <remarks>
/// It holds only who is owed the room's seats; what they are owed is the room as it stands when the debt is paid.
/// </remarks>
public interface IPvpResultScreenDeferralDomainService
{
    /// <summary>The member is owed the room's seats from now on.</summary>
    void Defer(SelectedSessionIdentity member, PvpRoomId room);

    /// <summary>Whether the member is owed seats of that room.</summary>
    bool Owes(SelectedSessionIdentity member, PvpRoomId room);

    /// <summary>Settles the member's debt: the room it is owed, or null when it was owed nothing.</summary>
    PvpRoomId? Release(SelectedSessionIdentity member);

    /// <summary>Forgets every debt of a room that is gone.</summary>
    void DropRoom(PvpRoomId room);
}
