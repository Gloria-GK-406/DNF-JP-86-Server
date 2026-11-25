using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Application.Pvp;

/// <summary>Rules shared by duel rooms irrespective of their battle mode.</summary>
internal static class PvpRoomRules
{
    /// <summary>Spec phase table: a fight still running this long past its time limit ends in a draw.</summary>
    public static readonly TimeSpan FightOverrun = TimeSpan.FromSeconds(30);

    /// <summary>Spec phase table: a fighter silent this long while fighting has left the battle.</summary>
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Spec phase table: the ranking screen waits this long before the battle is settled anyway.</summary>
    public static readonly TimeSpan RankTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Spec phase table: the result screen waits this long before the room waits again anyway.</summary>
    public static readonly TimeSpan ResultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>A21 matchmaking renders the rating near ten seconds and then finishes the result sequence.</summary>
    public static readonly TimeSpan MatchmakingResultTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Spec RQ-032: a time-out report is taken this much before the time limit, for clock drift.</summary>
    public static readonly TimeSpan TimeoutReportLead = TimeSpan.FromSeconds(5);

    /// <summary>
    /// An ended battle whose settlement still fails this long after it was due is dropped unsettled: a write that
    /// keeps failing for minutes will not succeed by retrying, and the room should not wait on it for ever.
    /// </summary>
    public static readonly TimeSpan SettlementGiveUp = TimeSpan.FromMinutes(5);

    /// <summary>Spec RQ-021: an invitation not answered within this is expired.</summary>
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromSeconds(30);

    public const int MinimumFighters = 2;

    /// <summary>Spec RQ-010: the client's password field holds at most eight bytes.</summary>
    public const int MaximumPasswordLength = 8;

    public static bool AcceptsPassword(int length) => length is > 0 and <= MaximumPasswordLength;

    /// <summary>Spec RQ-012: a lone member always keeps at least one seat someone else could take.</summary>
    public static bool AllowsClosing(int members, int openSeats) => members > 1 || openSeats > 1;

    /// <summary>Ownership passes to the next taken seat after the owner's, wrapping round.</summary>
    public static int? Successor(int ownerSeat, IReadOnlyList<int> remainingSeats)
    {
        if (remainingSeats.Count == 0) return null;
        return remainingSeats.OrderBy(seat => (seat - ownerSeat + PvpRoomSnapshot.SeatCount) % PvpRoomSnapshot.SeatCount).First();
    }
}
