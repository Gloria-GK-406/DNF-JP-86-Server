using Dfo.Server.Application.Configuration;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Application.Accounts;
using Xunit;

namespace Dfo.Server.Application.Tests.Accounts;

public sealed class DailyResetRuleTests
{
    private sealed class Settings(TimeSpan reset) : IServerConfigService
    {
        public T GetConfig<T>(string key) => key == "Game:DailyResetTime"
            ? (T)(object)reset : throw new InvalidOperationException(key);
    }
    [Theory]
    [InlineData(6, "2026-09-22T06:00:00+08:00", "2026-09-23T05:59:59+08:00", true)]
    [InlineData(6, "2026-09-22T05:59:59+08:00", "2026-09-23T05:59:59+08:00", false)]
    [InlineData(6, "2026-09-22T09:00:00+08:00", "2026-09-23T06:00:00+08:00", false)]
    [InlineData(6, "2026-09-23T06:00:00+08:00", "2026-09-23T06:00:00+08:00", true)]
    [InlineData(6, "2026-09-23T07:00:00+08:00", "2026-09-23T06:00:00+08:00", false)]
    [InlineData(6, "2026-09-22T09:00:00+08:00", "2026-09-23T07:59:59+08:00", false)]
    [InlineData(8, "2026-09-22T09:00:00+08:00", "2026-09-23T07:59:59+08:00", true)]
    [InlineData(8, "2026-09-22T09:00:00+08:00", "2026-09-23T08:00:00+08:00", false)]
    [InlineData(6, "2026-12-31T23:00:00+08:00", "2027-01-01T05:00:00+08:00", true)]
    [InlineData(6, "2028-02-29T09:00:00+08:00", "2028-03-01T05:00:00+08:00", true)]
    [InlineData(6, "2026-09-21T22:00:00Z", "2026-09-22T08:00:00+08:00", true)]
    public void Business_day_uses_configured_boundary_and_rejects_future_records(int hour, string recorded, string now, bool expected)
    {
        var rule = new DailyResetRuleService(new Settings(TimeSpan.FromHours(hour)));
        Assert.Equal(expected, rule.IsCurrentPeriod(DateTimeOffset.Parse(recorded), DateTimeOffset.Parse(now)));
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void Invalid_reset_time_is_rejected(int hour) =>
        Assert.Throws<InvalidOperationException>(() => new DailyResetRuleService(new Settings(TimeSpan.FromHours(hour))));
}
