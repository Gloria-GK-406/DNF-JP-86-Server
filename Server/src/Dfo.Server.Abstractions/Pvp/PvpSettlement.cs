using Dfo.Server.Data.Channel;

namespace Dfo.Server.Abstractions.Pvp;

/// <summary>What a duel channel lets a finished battle change.</summary>
/// <param name="RecordsResult">Whether wins, losses, draws and streaks are kept at all.</param>
/// <param name="AwardsGradeExperience">Whether the result moves grade experience.</param>
/// <param name="DailyWinExperienceCap">Wins per game day that still earn experience.</param>
/// <param name="DailyLossExperienceCap">Losses per game day that still cost experience.</param>
public sealed record PvpChannelEntitlements(
    bool RecordsResult,
    bool AwardsGradeExperience,
    int DailyWinExperienceCap,
    int DailyLossExperienceCap);

public enum PvpOutcome : byte
{
    Win,
    Loss,
    Draw,
}

/// <summary>Everything one fighter's settlement is decided from.</summary>
/// <param name="Grade">
/// The stored grade; only read when <paramref name="Experience"/> is null. A grade above the archive's top, which a
/// record from before the grade table was enforced may hold, starts at the top grade's lower bound.
/// </param>
/// <param name="Experience">
/// Accumulated grade experience, or null for a record that has never had any, which starts at
/// the lower bound of <paramref name="Grade"/>.
/// </param>
/// <param name="Escaped">An escaped fighter settles as a loss whatever the battle's outcome.</param>
/// <param name="WinsEarlierToday">Wins already counted on this game day, not including this battle.</param>
/// <param name="LossesEarlierToday">Losses already counted on this game day, not including this battle.</param>
public sealed record PvpSettlementFacts(
    byte Grade,
    long? Experience,
    PvpOutcome Outcome,
    bool Escaped,
    int WinsEarlierToday,
    int LossesEarlierToday,
    PvpChannelEntitlements Entitlements);

/// <param name="CountsTowardDaily">Whether this battle adds one to today's win or loss count.</param>
public sealed record PvpSettlementDecision(
    byte Grade,
    long Experience,
    long ExperienceChange,
    bool CountsTowardDaily);

/// <summary>One grade of the archive's grade table.</summary>
/// <param name="LowerBound">The experience at which this grade starts.</param>
/// <param name="UpperBound">The experience at which the next grade starts; for the top grade, the archive's sentinel.</param>
/// <param name="LossExperience">Zero or negative.</param>
public sealed record PvpGradeStep(byte Grade, long LowerBound, long UpperBound, int WinExperience, int LossExperience);

/// <param name="Grades">Every grade from 0 to <paramref name="MaximumGrade"/>, in order.</param>
public sealed record PvpGradeCatalog(
    IReadOnlyList<PvpGradeStep> Grades,
    byte MaximumGrade,
    TimeSpan NormalBattleTime,
    TimeSpan RelayBattleTime);

public interface IPvpGradeDefinitionSource { PvpGradeCatalog Load(); }

public interface IPvpSettlementRuleService
{
    /// <summary>
    /// What a duel channel records. A free-duel channel records nothing whatever its environment; a ranked or
    /// practice duel channel answers by its login environment, and throws for an environment without a row,
    /// as it does for a channel that is not a duel channel at all.
    /// </summary>
    PvpChannelEntitlements EntitlementsOf(ChannelEntryIdentity channel);

    PvpSettlementDecision Decide(PvpSettlementFacts facts);

    byte GradeOf(long experience);

    /// <summary>The archive's top grade; <see cref="LowerBound"/> and <see cref="UpperBound"/> refuse any grade above it.</summary>
    byte MaximumGrade { get; }

    long LowerBound(byte grade);
    long UpperBound(byte grade);

    /// <summary>How long a battle of this mode runs before the client reports a time-out.</summary>
    TimeSpan BattleTimeLimit(PvpBattleMode mode);

    /// <summary>The game day a battle started at belongs to; daily counts are kept per game day.</summary>
    DateOnly GameDayOf(DateTimeOffset startedAt);
}
