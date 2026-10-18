namespace Dfo.Server.Abstractions.Scheduling;

public interface IScheduledTaskSchedule
{
    DateTimeOffset LastOccurrence(DateTimeOffset at);
    DateTimeOffset NextOccurrence(DateTimeOffset after);
}

public enum ScheduledTaskMissedRunPolicy { Skip, RunOnce }

public sealed record ScheduledTaskContext(IServiceProvider Services, string TaskId,
    DateTimeOffset ScheduledAt, DateTimeOffset StartedAt, int Attempt);

public delegate ValueTask ScheduledTaskCallback(ScheduledTaskContext context, CancellationToken ct);

public sealed record ScheduledTaskRegistration(string Id, IScheduledTaskSchedule Schedule, ScheduledTaskCallback Execute)
{
    public ScheduledTaskMissedRunPolicy MissedRun { get; init; } = ScheduledTaskMissedRunPolicy.Skip;
    public int MaximumAttempts { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);
}

public sealed record ScheduledTaskSchedulerOptions(int MaximumConcurrency = 4);
