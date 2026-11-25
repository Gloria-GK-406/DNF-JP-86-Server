using System.Collections.Immutable;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Pvp;

/// <summary>A character's duel record as it stands.</summary>
/// <param name="Experience">
/// Null for a record no recorded battle has touched yet; it reads as the lower bound of
/// <paramref name="Grade"/>, which only the settlement rule service can say.
/// </param>
/// <param name="RatingGrade">Kept as stored; no battle changes it.</param>
public sealed record PvpRecord(
    byte Grade,
    byte RatingGrade,
    long? Experience,
    int Wins,
    int Losses,
    int Draws,
    int WinStreak,
    int PeakWinStreak);

/// <summary>Battles of one game day that counted toward the daily experience caps.</summary>
public sealed record PvpDailyCounts(int Wins, int Losses);

/// <summary>How a battle ended, as a whole.</summary>
public enum PvpMatchOutcomeKind : byte
{
    Decided = 0,
    Draw = 1,

    /// <summary>Ended before it could be decided; changes no record.</summary>
    Cancelled = 2,
}

/// <summary>One fighter's part in a battle being settled.</summary>
/// <param name="Outcome">The fighter's settled outcome; a fighter who fled settles as a loss.</param>
/// <param name="Decision">
/// The settlement rule service's answer for this fighter. Its grade and experience become the
/// record's when the battle records results.
/// </param>
/// <param name="UpdatesRecord">False for a total-match representative who did not enter combat but needs a result screen.</param>
public sealed record PvpFighterSettlement(
    CharacterId Character,
    byte Seat,
    byte Team,
    PvpOutcome Outcome,
    int Kills,
    int Deaths,
    bool Fled,
    PvpSettlementDecision Decision,
    bool UpdatesRecord = true)
{
    public PvpRatingChange? Rating { get; init; }
}

/// <summary>Everything a finished battle writes, once.</summary>
/// <param name="ChannelEnvironment">The duel channel's login environment; null for a free-duel channel configured without one.</param>
/// <param name="Winner">The winning seat or team, as the battle mode counts them; null unless decided.</param>
/// <param name="RecordsResult">Whether the channel keeps records: when false the fighters' records are left as they are.</param>
/// <param name="GameDay">The game day the battle started on, which the daily counts are kept by.</param>
public sealed record PvpMatchSettlementRecord(
    Guid MatchId,
    ushort RoomId,
    int ListenerPort,
    byte? ChannelEnvironment,
    PvpBattleMode Mode,
    PvpMatchOutcomeKind OutcomeKind,
    byte? Winner,
    bool RecordsResult,
    DateOnly GameDay,
    DateTimeOffset CompletedAt,
    IReadOnlyList<PvpFighterSettlement> Fighters)
{
    public IReadOnlyDictionary<AccountId, PvpRatingChange> TotalMatchRatings { get; init; } =
        ImmutableDictionary<AccountId, PvpRatingChange>.Empty;
}

/// <summary>One fighter's settled result, as stored.</summary>
/// <param name="WinStreak">The fighter's win streak after this battle.</param>
public sealed record PvpFighterResult(
    CharacterId Character,
    byte Seat,
    byte Team,
    PvpOutcome Outcome,
    int Kills,
    int Deaths,
    bool Fled,
    bool CountsTowardDaily,
    byte PreviousGrade,
    byte Grade,
    long ExperienceChange,
    long Experience,
    int WinStreak)
{
    public PvpRatingChange? Rating { get; init; }
}

/// <summary>What settling a battle did.</summary>
/// <param name="Results">Every fighter's stored result, by seat.</param>
public abstract record PvpSettleOutcome(IReadOnlyList<PvpFighterResult> Results)
{
    /// <summary>This call wrote the battle.</summary>
    public sealed record Created(IReadOnlyList<PvpFighterResult> Results) : PvpSettleOutcome(Results);

    /// <summary>The battle had been written before; nothing was written now.</summary>
    public sealed record AlreadySettled(IReadOnlyList<PvpFighterResult> Results) : PvpSettleOutcome(Results);
}

/// <summary>Duel records, battles and their results.</summary>
public interface IPvpRecordDomainStore
{
    /// <summary>The character's record; a character without one is refused as inconsistent data.</summary>
    ValueTask<PvpRecord> ReadAsync(CharacterId character, CancellationToken cancellationToken);
    ValueTask<PvpRatingStanding> ReadRatingAsync(CharacterId character, int seasonId, CancellationToken cancellationToken);
    ValueTask<PvpRatingStanding> ReadTotalMatchRatingAsync(CharacterId character, int seasonId,
        CancellationToken cancellationToken);

    /// <summary>The character's battles of that game day that counted toward the daily caps.</summary>
    ValueTask<PvpDailyCounts> DailyCountsAsync(
        CharacterId character, DateOnly gameDay, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the battle, every fighter's result and, when it records results, every fighter's
    /// record -- unless the battle was written before, in which case it reads back what was
    /// written and changes nothing.
    /// </summary>
    /// <remarks>
    /// Only inside a use case: the battle and the records change together or not at all, and the
    /// match identifier is what makes a second call, including one after a restart, harmless.
    /// </remarks>
    ValueTask<PvpSettleOutcome> SettleAsync(
        PvpMatchSettlementRecord settlement, CancellationToken cancellationToken);
}

/// <summary>A character's duel record as the client shows it: the grade's experience band filled in.</summary>
/// <param name="Experience">The accumulated grade experience; a record no battle has touched reads as its grade's lower bound.</param>
/// <param name="LowerBound">Where the record's grade starts.</param>
/// <param name="UpperBound">Where the next grade starts; for the top grade, the archive's sentinel.</param>
public sealed record PvpRecordView(
    byte Grade,
    byte RatingGrade,
    long Experience,
    long LowerBound,
    long UpperBound,
    int Wins,
    int Losses)
{
    public PvpRatingStanding Rating { get; init; } = PvpRatingStanding.Initial;
    public PvpRatingStanding TotalMatchRating { get; init; } = PvpRatingStanding.Initial;
}

/// <summary>Duel records as their owners and the client read them.</summary>
public interface IPvpRecordDomainService
{
    /// <summary>The character's record with its grade's band; read-only, inside whatever unit the caller has open.</summary>
    ValueTask<PvpRecordView> ViewAsync(CharacterId character, CancellationToken cancellationToken);
    PvpExperienceBand ExperienceBandOf(byte grade);
}

public readonly record struct PvpExperienceBand(long LowerBound, long UpperBound);
