using Dfo.Server.Abstractions.Configuration;

namespace Dfo.Server.Application.Configuration;

internal sealed class DailyResetRuleService(IServerConfigService settings) : IDailyResetRuleService
{
    private static readonly TimeSpan BusinessOffset = TimeSpan.FromHours(8);
    private readonly TimeSpan resetTime = Validate(settings.GetConfig<TimeSpan>("Game:DailyResetTime"));

    private static TimeSpan Validate(TimeSpan value) => value >= TimeSpan.Zero && value < TimeSpan.FromDays(1)
        ? value : throw new InvalidOperationException("Game:DailyResetTime must be between 00:00:00 and 24:00:00 (exclusive).");

    public DateTimeOffset PeriodStart(DateTimeOffset now)
    {
        var local = now.ToOffset(BusinessOffset);
        var start = new DateTimeOffset(local.Date + resetTime, BusinessOffset);
        if (local < start) start = start.AddDays(-1);
        return start.ToUniversalTime();
    }

    public bool IsCurrentPeriod(DateTimeOffset recordedAt, DateTimeOffset now) =>
        recordedAt <= now && recordedAt >= PeriodStart(now);
}
