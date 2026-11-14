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

    /// <summary>How long the fighters have to finish loading once a battle starts.</summary>
    TimeSpan LoadingTimeout { get; }

    /// <summary>How long an invitation into a room stays open.</summary>
    TimeSpan InvitationLifetime { get; }

    /// <summary>The team a member joining the room takes, given the teams of those already seated.</summary>
    PvpTeam TeamForNewcomer(PvpBattleMode mode, IReadOnlyList<PvpTeam> seated);

    /// <summary>The teams of the seated members, in seat order, once the room switches to <paramref name="mode"/>.</summary>
    IReadOnlyList<PvpTeam> Reassign(PvpBattleMode mode, int members);

    /// <summary>Whether a member may move to <paramref name="team"/> in a room of this mode.</summary>
    bool AcceptsTeam(PvpBattleMode mode, PvpTeam team);

    /// <summary>Why the owner may not start a battle now, or null when it may.</summary>
    PvpRoomRefusal? RefuseStart(PvpBattleMode mode, IReadOnlyList<PvpRoomMemberFacts> members);

    /// <summary>Whether the owner may close one more seat, given how many members and open seats the room has.</summary>
    bool AllowsClosing(int members, int openSeats);

    /// <summary>The seat that becomes owner when the owner leaves, or null when nobody is left.</summary>
    int? Successor(int ownerSeat, IReadOnlyList<int> remainingSeats);
}
