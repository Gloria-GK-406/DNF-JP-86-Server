using Dfo.Server.Abstractions.Scheduling;

namespace Dfo.Server.Application.Scheduling;

public sealed class DailyTaskSchedule : IScheduledTaskSchedule
{
    public DailyTaskSchedule(TimeOnly time, TimeSpan utcOffset)
    {
        if (utcOffset < TimeSpan.FromHours(-14) || utcOffset > TimeSpan.FromHours(14)
            || utcOffset.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new ArgumentOutOfRangeException(nameof(utcOffset));
        Time = time; UtcOffset = utcOffset;
    }
    public TimeOnly Time { get; }
    public TimeSpan UtcOffset { get; }
    public DateTimeOffset LastOccurrence(DateTimeOffset at)
    {
        var local = at.ToOffset(UtcOffset);
        var today = new DateTimeOffset(local.Date + Time.ToTimeSpan(), UtcOffset);
        return (today > at ? today.AddDays(-1) : today).ToUniversalTime();
    }
    public DateTimeOffset NextOccurrence(DateTimeOffset after) => LastOccurrence(after).AddDays(1);
}

public sealed class IntervalTaskSchedule : IScheduledTaskSchedule
{
    public IntervalTaskSchedule(TimeSpan interval, DateTimeOffset? anchor = null)
    {
        if (interval < TimeSpan.FromMilliseconds(1)) throw new ArgumentOutOfRangeException(nameof(interval));
        Interval = interval; Anchor = (anchor ?? DateTimeOffset.UnixEpoch).ToUniversalTime();
    }
    public TimeSpan Interval { get; }
    public DateTimeOffset Anchor { get; }
    public DateTimeOffset LastOccurrence(DateTimeOffset at)
    {
        var periods = Math.DivRem(at.UtcTicks - Anchor.UtcTicks, Interval.Ticks, out var remainder);
        if (remainder < 0) periods--;
        return Anchor.AddTicks(checked(periods * Interval.Ticks));
    }
    public DateTimeOffset NextOccurrence(DateTimeOffset after) => LastOccurrence(after).Add(Interval);
}
