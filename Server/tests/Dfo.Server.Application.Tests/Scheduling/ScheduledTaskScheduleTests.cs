using Dfo.Server.Application.Scheduling;

namespace Dfo.Server.Application.Tests.Scheduling;

public sealed class ScheduledTaskScheduleTests
{
    [Theory]
    [InlineData(5, 59, -1)]
    [InlineData(6, 0, 0)]
    [InlineData(6, 1, 0)]
    public void Daily_boundary_uses_explicit_business_offset_and_strict_next(int hour, int minute, int day)
    {
        var offset = TimeSpan.FromHours(8);
        var schedule = new DailyTaskSchedule(new(6, 0), offset);
        var at = new DateTimeOffset(2026, 10, 14, hour, minute, 0, offset);
        var expected = new DateTimeOffset(2026, 10, 14, 6, 0, 0, offset).AddDays(day);
        Assert.Equal(expected, schedule.LastOccurrence(at));
        Assert.Equal(expected.AddDays(1), schedule.NextOccurrence(at));
        Assert.True(schedule.NextOccurrence(at) > at);
    }

    [Theory]
    [InlineData(-30, -60, 0)]
    [InlineData(0, 0, 60)]
    [InlineData(30, 0, 60)]
    [InlineData(180, 180, 240)]
    public void Fixed_interval_keeps_its_anchor_across_negative_and_positive_periods(int now, int last, int next)
    {
        var anchor = DateTimeOffset.UnixEpoch;
        var schedule = new IntervalTaskSchedule(TimeSpan.FromMinutes(1), anchor);
        Assert.Equal(anchor.AddSeconds(last), schedule.LastOccurrence(anchor.AddSeconds(now)));
        Assert.Equal(anchor.AddSeconds(next), schedule.NextOccurrence(anchor.AddSeconds(now)));
    }

    [Fact]
    public void Invalid_time_rules_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntervalTaskSchedule(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DailyTaskSchedule(new(6, 0), TimeSpan.FromHours(15)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DailyTaskSchedule(new(6, 0), TimeSpan.FromSeconds(1)));
    }
}
