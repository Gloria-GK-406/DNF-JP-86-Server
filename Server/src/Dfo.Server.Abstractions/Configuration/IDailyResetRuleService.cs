namespace Dfo.Server.Abstractions.Configuration;

/// <summary>
/// The one game day: every daily count, refill and refresh starts its day at the configured reset
/// time, never at midnight.
/// </summary>
public interface IDailyResetRuleService
{
    /// <summary>The offset the game calendar is kept in; dates and the reset time are read there.</summary>
    static readonly TimeSpan BusinessOffset = TimeSpan.FromHours(8);

    DateTimeOffset PeriodStart(DateTimeOffset now);
    bool IsCurrentPeriod(DateTimeOffset recordedAt, DateTimeOffset now);

    /// <summary>
    /// The game day <paramref name="now"/> falls in, named by the calendar date its period started on.
    /// </summary>
    /// <remarks>
    /// The key every per-day ledger is stored under. Before the reset time it is still yesterday:
    /// 03:00 belongs to the day that began at the previous 06:00.
    /// </remarks>
    DateOnly PeriodDay(DateTimeOffset now) =>
        DateOnly.FromDateTime(PeriodStart(now).ToOffset(BusinessOffset).DateTime);
}
