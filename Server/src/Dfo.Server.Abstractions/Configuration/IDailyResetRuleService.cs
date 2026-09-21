namespace Dfo.Server.Abstractions.Configuration;

public interface IDailyResetRuleService
{
    DateTimeOffset PeriodStart(DateTimeOffset now);
    bool IsCurrentPeriod(DateTimeOffset recordedAt, DateTimeOffset now);
}
