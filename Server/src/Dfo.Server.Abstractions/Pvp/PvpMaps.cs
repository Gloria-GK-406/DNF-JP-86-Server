namespace Dfo.Server.Abstractions.Pvp;

/// <summary>The archive's duel map order; a room's map is an index into it.</summary>
/// <param name="Order">Map ids by index. Index 0 is the random choice rather than a map a room plays on.</param>
public sealed record PvpMapCatalog(IReadOnlyList<int> Order);

public interface IPvpMapDefinitionSource { PvpMapCatalog Load(); }

/// <summary>One seated member, as the start rule reads the room.</summary>
public sealed record PvpRoomMemberFacts(PvpTeam Team, bool Ready, bool IsOwner);

public interface IPvpRoomRuleService
{
    /// <summary>Whether a room may be set to this map index.</summary>
    bool AcceptsMap(int index);

    /// <summary>
    /// The map index a battle starts on: the room's own, or for index 0 a map drawn from the
    /// rest with <paramref name="seed"/>. Throws for an index <see cref="AcceptsMap"/> refuses.
    /// </summary>
    byte StartMap(int index, int seed);

    /// <summary>Whether a room may be locked with this password.</summary>
    bool AcceptsPassword(System.Collections.Immutable.ImmutableArray<byte> password);

    /// <summary>How long past the battle's time limit a fight may run before the server ends it in a draw.</summary>
    TimeSpan FightOverrun { get; }

    /// <summary>How long a fighting fighter may go without a heartbeat before it has left the battle.</summary>
    TimeSpan HeartbeatTimeout { get; }

    /// <summary>How long the ranking screen waits for the fighters before the battle is settled.</summary>
    TimeSpan RankTimeout { get; }

    /// <summary>How long the result screen waits for the fighters before the room waits again.</summary>
    TimeSpan ResultTimeout(PvpRoomOrigin origin);

    /// <summary>How long before the battle's time limit a client's time-out report is already taken.</summary>
    TimeSpan TimeoutReportLead { get; }

    /// <summary>How long after it was due an ended battle that cannot be settled is given up.</summary>
    TimeSpan SettlementGiveUp { get; }

    /// <summary>How long an invitation into a room stays open.</summary>
    TimeSpan InvitationLifetime { get; }

    /// <summary>Whether the owner may close one more seat, given how many members and open seats the room has.</summary>
    bool AllowsClosing(int members, int openSeats);

    /// <summary>The seat that becomes owner when the owner leaves, or null when nobody is left.</summary>
    int? Successor(int ownerSeat, IReadOnlyList<int> remainingSeats);
}
