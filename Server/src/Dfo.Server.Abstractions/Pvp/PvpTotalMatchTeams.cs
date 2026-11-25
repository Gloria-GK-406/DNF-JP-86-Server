using System.Collections.Immutable;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Pvp;

public sealed record PvpTotalMatchTeam(string Name, ImmutableArray<CharacterId> Members)
{
    public const int MemberCount = 3;
    public const int NameCharacterLimit = 12;
    public const int NameByteLimit = NameCharacterLimit * 4;
}

/// <summary>A roster projection uses current roster positions. Storage uses stable character IDs.</summary>
public sealed record PvpTotalMatchTeamView(string Name, ImmutableArray<byte> Positions, CharacterId Representative);

public sealed record PvpTotalMatchCandidate(
    ActiveCharacterState Character,
    CharacterPublicEntryState Public,
    IReadOnlyList<InventoryItemState> Equipment,
    CharacterSkillPageState DuelSkills);

public sealed record PvpTotalMatchLineup(string TeamName, ImmutableArray<PvpTotalMatchCandidate> Candidates)
{
    public int GroupPoints { get; init; }
}

public enum PvpTotalMatchPhase
{
    Loading,
    Selecting,
    Fighting,
    BetweenRounds,
}

public sealed record PvpTotalMatchSide(int Seat, PvpTotalMatchLineup Lineup, byte? Choice = null, byte Defeated = 0)
{
    public byte Appeared { get; init; }
    public ImmutableDictionary<byte, int> CandidateKills { get; init; } = ImmutableDictionary<byte, int>.Empty;

    public bool CanSelect(int ordinal) => (uint)ordinal < (uint)Lineup.Candidates.Length
        && (Defeated & (1 << ordinal)) == 0;

    public bool HasRemaining => Defeated != (1 << Lineup.Candidates.Length) - 1;
}

public sealed record PvpTotalMatchBattle(
    PvpTotalMatchPhase Phase, ImmutableArray<PvpTotalMatchSide> Sides, DateTimeOffset PreparedAt,
    TimeSpan RoundLimit, DateTimeOffset RoundOpensAt = default) : PvpMatchModeState
{
    public bool WaitForOpeningHeartbeats { get; init; }
    public byte OpeningHeartbeatSeats { get; init; }
}

public sealed record PvpTotalMatchAppearance(int Seat, CharacterId Character, int Kills, int Deaths);

public interface IPvpTotalMatchCandidateQueryDomainService
{
    ValueTask<PvpTotalMatchLineup?> ReadAsync(
        SelectedSessionIdentity session, DateTimeOffset preparedAt, CancellationToken cancellationToken);
}

public enum PvpTotalMatchTeamRefusal
{
    None,
    NotAvailable,
    InvalidName,
    NameTaken,
    InvalidComposition,
}

public interface IPvpTotalMatchTeamDomainService
{
    ValueTask<PvpTotalMatchTeamView?> ReadAsync(AccountId account, CancellationToken cancellationToken);
    ValueTask<PvpTotalMatchTeamRefusal> CheckNameAsync(
        SelectedSessionIdentity session, byte[] name, CancellationToken cancellationToken);
    ValueTask<(PvpTotalMatchTeamView? Team, PvpTotalMatchTeamRefusal? Refusal)> SaveAsync(
        SelectedSessionIdentity session, byte[] name, ImmutableArray<byte> positions, CancellationToken cancellationToken);
}

public interface IPvpTotalMatchTeamDomainStore
{
    ValueTask<PvpTotalMatchTeam?> ReadAsync(AccountId account, CancellationToken cancellationToken);
    ValueTask<bool> IsNameAvailableAsync(AccountId account, string name, CancellationToken cancellationToken);
    ValueTask SaveAsync(AccountId account, PvpTotalMatchTeam team, CancellationToken cancellationToken);
}
