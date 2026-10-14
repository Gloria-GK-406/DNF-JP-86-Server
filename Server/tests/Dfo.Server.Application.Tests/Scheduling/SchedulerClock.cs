namespace Dfo.Server.Application.Tests.Scheduling;

internal sealed class SchedulerClock : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ManualTimer> timers = [];
    private DateTimeOffset now = new(2026, 10, 14, 21, 59, 0, TimeSpan.Zero);
    private long elapsed;
    private int timerVersion;
    public int TimerVersion => Volatile.Read(ref timerVersion);
    public int ActiveTimers { get { lock (gate) return timers.Count(x => x.Due is not null); } }
    public override DateTimeOffset GetUtcNow() { lock (gate) return now; }
    public override long GetTimestamp() { lock (gate) return elapsed; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public int Advance(TimeSpan amount)
    {
        ManualTimer[] fire;
        lock (gate)
        {
            now += amount; elapsed += amount.Ticks;
            fire = timers.Where(x => x.Due <= elapsed).ToArray();
            foreach (var timer in fire) timer.Due = timer.Period > TimeSpan.Zero ? elapsed + timer.Period.Ticks : null;
        }
        foreach (var timer in fire) timer.Fire();
        return fire.Length;
    }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (gate) timers.Add(timer);
        timer.Change(dueTime, period);
        Interlocked.Increment(ref timerVersion);
        return timer;
    }
    private sealed class ManualTimer(SchedulerClock owner, TimerCallback callback, object? state) : ITimer
    {
        private bool disposed;
        public long? Due { get; set; }
        public TimeSpan Period { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                if (disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.elapsed + dueTime.Ticks;
                Period = period;
                return true;
            }
        }
        public void Fire() { lock (owner.gate) { if (disposed) return; } callback(state); }
        public void Dispose() { lock (owner.gate) { disposed = true; Due = null; owner.timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
