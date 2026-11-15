using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Pvp;

/// <summary>The phase reports a fighter has made in the current battle.</summary>
[Flags]
public enum PvpFighterReports : byte
{
    None = 0,
    RankAcknowledged = 1,
    ResultAcknowledged = 2,
}

/// <summary>Why a member left its room's battle.</summary>
public enum PvpDepartureReason
{
    /// <summary>The session ended: its connection closed, it went back to character selection, or a new selection replaced it.</summary>
    /// <remarks>
    /// A fighter that only stopped sending heartbeats is not a departure: the room's own deadline counts it fled and
    /// it keeps its seat (<see cref="IPvpRoomDomainService.AdvanceDueAsync"/>).
    /// </remarks>
    Disconnected,
}

/// <summary>How a battle ended, frozen at that moment.</summary>
/// <param name="Kind"><see cref="PvpMatchOutcomeKind.Decided"/> or <see cref="PvpMatchOutcomeKind.Draw"/>; a cancelled battle has no end to settle.</param>
/// <param name="Winner">The winning seat, or the winning team for a team mode, as the battle mode counts sides; null for a draw.</param>
public sealed record PvpMatchTerminal(PvpMatchOutcomeKind Kind, byte? Winner, DateTimeOffset At);

/// <summary>A death a fighter reported.</summary>
/// <param name="KillerSeat">The seat credited with the kill; null when nobody was.</param>
public sealed record PvpDeath(int VictimSeat, int? KillerSeat);

/// <summary>
/// An ended battle as settlement reads it: frozen when the battle ended, kept until it is settled, and kept
/// even when its room is gone.
/// </summary>
/// <param name="Fighters">Every fighter as the battle ended: kills, deaths and who fled no longer change.</param>
/// <param name="SettleBy">When the battle is settled at the latest, acknowledged or not.</param>
public sealed record PvpMatchConclusion(
    PvpMatchId MatchId,
    PvpRoomId RoomId,
    ushort RoomNumber,
    ChannelEntryIdentity Channel,
    PvpBattleMode Mode,
    DateTimeOffset StartedAt,
    PvpMatchTerminal Terminal,
    ImmutableArray<PvpFighter> Fighters,
    DateTimeOffset SettleBy);

/// <summary>What the duel deadline scheduler finds due.</summary>
/// <param name="Abandoned">Ended battles whose settlement kept failing past the give-up time; they are dropped unsettled.</param>
/// <param name="Next">
/// When to look again: the earliest deadline still ahead, or sooner when something due is still waiting after a
/// failed attempt, backing off the longer it has been overdue. Null when nothing waits.
/// </param>
public sealed record PvpDeadlineScan(
    IReadOnlyList<PvpRoomId> DueRooms,
    IReadOnlyList<PvpMatchId> DueSettlements,
    IReadOnlyList<PvpMatchId> Abandoned,
    DateTimeOffset? Next);

/// <summary>
/// Receives a room member's departure from selected-session cleanup, once the transaction that made it has
/// committed; a departure whose transaction rolls back never arrives.
/// </summary>
public interface IPvpRoomDepartureSink
{
    /// <param name="settled">
    /// The settlement the departure led to, when the cleanup could settle it on its own; null when there was none
    /// to make, or when it is left to the deadline scheduler (inside a caller's transaction, or after it failed).
    /// </param>
    ValueTask DepartedAsync(
        PvpRoomOutcome.Changed departure, PvpSettlementOutcome? settled, CancellationToken cancellationToken);
}

/// <summary>What settling a battle did.</summary>
public abstract record PvpSettlementOutcome
{
    private PvpSettlementOutcome()
    {
    }

    /// <summary>No ended battle of that identity waits for settlement: it was never ended, or it is settled.</summary>
    public sealed record NotPending : PvpSettlementOutcome;

    /// <param name="Stored">
    /// The results as written: <see cref="PvpSettleOutcome.Created"/> when this call wrote them,
    /// <see cref="PvpSettleOutcome.AlreadySettled"/> when they had been written before.
    /// </param>
    /// <param name="Room">The room moving to its result screen; null when the room is gone.</param>
    public sealed record Settled(PvpMatchConclusion Conclusion, PvpSettleOutcome Stored, PvpRoomOutcome.Changed? Room)
        : PvpSettlementOutcome;
}

/// <summary>Settles ended battles: decides every fighter's result, writes it once, and publishes it once.</summary>
public interface IPvpSettlementDomainService
{
    /// <remarks>
    /// Joins the caller's writable transaction: the results, the event and the room's move to its result screen
    /// commit together or not at all.
    /// </remarks>
    ValueTask<PvpSettlementOutcome> SettleAsync(PvpMatchId match, CancellationToken cancellationToken);
}

/// <summary>A battle's results were written for the first time.</summary>
public sealed record PvpMatchSettled(
    Guid MatchId,
    int ListenerPort,
    ushort RoomNumber,
    PvpBattleMode Mode,
    PvpMatchOutcomeKind Outcome,
    byte? Winner,
    bool RecordsResult,
    DateOnly GameDay,
    IReadOnlyList<PvpMatchSettledFighter> Fighters) : IDomainEvent;

public sealed record PvpMatchSettledFighter(
    CharacterId Character,
    PvpOutcome Outcome,
    bool Fled,
    byte Grade,
    long ExperienceChange);
