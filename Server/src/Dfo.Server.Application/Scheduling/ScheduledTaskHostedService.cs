using System.Threading.Channels;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Scheduling;

internal sealed class ScheduledTaskHostedService : BackgroundService
{
    private sealed class Job(ScheduledTaskRegistration definition, DateTimeOffset next)
    {
        public ScheduledTaskRegistration Definition { get; } = definition;
        public DateTimeOffset Next { get; set; } = next;
        public DateTimeOffset? Pending { get; set; }
        public Task? Execution { get; set; }
        public bool Disabled { get; set; }
        public bool Started { get; set; }
        public DateTimeOffset Requested { get; set; }
    }

    private readonly PriorityQueue<Job, long> due = new();
    private readonly Channel<Job> completed = System.Threading.Channels.Channel.CreateUnbounded<Job>(new()
    { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false });
    private readonly HashSet<Task> executions = [];
    private readonly IServiceScopeFactory scopes;
    private readonly IGameplayQueue queue;
    private readonly TimeProvider clock;
    private readonly ILogger<ScheduledTaskHostedService> logger;

    public ScheduledTaskHostedService(IEnumerable<ScheduledTaskRegistration> registrations,
        IServiceScopeFactory scopes, IGameplayQueue queue, TimeProvider clock,
        ILogger<ScheduledTaskHostedService> logger)
    {
        this.scopes = scopes; this.queue = queue; this.clock = clock; this.logger = logger;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var now = clock.GetUtcNow();
        foreach (var definition in registrations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
            ArgumentNullException.ThrowIfNull(definition.Schedule);
            ArgumentNullException.ThrowIfNull(definition.Execute);
            if (!ids.Add(definition.Id)) throw new InvalidOperationException($"Duplicate scheduled task '{definition.Id}'.");
            if (!Enum.IsDefined(definition.MissedRun) || definition.MaximumAttempts is < 1 or > 10
                || definition.RetryDelay < TimeSpan.Zero || definition.RetryDelay > TimeSpan.FromDays(1))
                throw new ArgumentException($"Invalid execution policy for '{definition.Id}'.");
            var (last, next) = Occurrences(definition.Schedule, now);
            var job = new Job(definition, definition.MissedRun == ScheduledTaskMissedRunPolicy.RunOnce ? last : next);
            due.Enqueue(job, job.Next.UtcTicks);
        }
    }

    private static (DateTimeOffset Last, DateTimeOffset Next) Occurrences(IScheduledTaskSchedule schedule, DateTimeOffset now)
    {
        var last = schedule.LastOccurrence(now);
        var next = schedule.NextOccurrence(now);
        if (last > now || next <= now)
            throw new InvalidOperationException("A schedule must return a past/present occurrence and a strictly future occurrence.");
        return (last, next);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (due.Count == 0) return;
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var now = clock.GetUtcNow();
                // Observe due occurrences before completions: both wakeups can arrive together.
                while (!stopping.IsCancellationRequested && due.TryPeek(out var job, out var when) && when <= now.UtcTicks)
                {
                    due.Dequeue();
                    DateTimeOffset latest;
                    try
                    {
                        (latest, job.Next) = Occurrences(job.Definition.Schedule, now);
                        due.Enqueue(job, job.Next.UtcTicks);
                    }
                    catch (Exception error)
                    {
                        job.Disabled = true; job.Pending = null;
                        logger.LogError(error, "Disabled scheduled task {TaskId}: invalid schedule", job.Definition.Id);
                        continue;
                    }
                    if (job.Execution is not null)
                    {
                        if (job.Definition.MissedRun == ScheduledTaskMissedRunPolicy.RunOnce)
                            lock (job)
                            {
                                if (job.Started) job.Pending = latest;
                                else job.Requested = latest;
                            }
                    }
                    else Start(job, latest, stopping.Token);
                }
                while (completed.Reader.TryRead(out var job))
                {
                    executions.Remove(job.Execution!);
                    job.Execution = null;
                    var pending = job.Pending;
                    job.Pending = null;
                    if (!stopping.IsCancellationRequested && !job.Disabled && pending is { } missed && missed <= clock.GetUtcNow())
                        Start(job, missed, stopping.Token);
                }
                if (due.Count == 0 && executions.Count == 0) return;
                await WaitAsync(stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        finally
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(executions).ConfigureAwait(false);
        }
    }

    private void Start(Job job, DateTimeOffset scheduledAt, CancellationToken ct)
    {
        job.Started = false; job.Requested = scheduledAt;
        // The callback runs as an executor job, so the timing loop only queues it here and never waits for it.
        job.Execution = RunAsync(job, ct);
        executions.Add(job.Execution);
    }

    private async Task RunAsync(Job job, CancellationToken ct)
    {
        try
        {
            for (var attempt = 1; attempt <= job.Definition.MaximumAttempts; attempt++)
            {
                // Read again when the attempt starts: a queued run that comes due again runs once, at the latest time.
                DateTimeOffset scheduledAt;
                lock (job) scheduledAt = job.Requested;
                try
                {
                    await GameplayTimerWork.RunAsync(queue, $"timer:ScheduledTaskHostedService/{job.Definition.Id}", async token =>
                    {
                        lock (job) { job.Started = true; scheduledAt = job.Requested; }
                        token.ThrowIfCancellationRequested();
                        await using (var scope = scopes.CreateAsyncScope())
                            await job.Definition.Execute(new(scope.ServiceProvider, job.Definition.Id,
                                scheduledAt, clock.GetUtcNow(), attempt), token).ConfigureAwait(false);
                    }, ct).ConfigureAwait(false);
                    logger.LogInformation("Scheduled task {TaskId} completed due={ScheduledAt} attempt={Attempt}",
                        job.Definition.Id, scheduledAt, attempt);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception error)
                {
                    if (attempt == job.Definition.MaximumAttempts)
                    {
                        // The executor logged the attempt's own failure with its stack (F-30).
                        if (GameplayJob.EndedAJob(error))
                            logger.LogWarning("Scheduled task {TaskId} exhausted {Attempts} attempts due={ScheduledAt}",
                                job.Definition.Id, attempt, scheduledAt);
                        else
                            logger.LogError(error, "Scheduled task {TaskId} exhausted {Attempts} attempts due={ScheduledAt}",
                                job.Definition.Id, attempt, scheduledAt);
                        return;
                    }
                    logger.LogWarning(error, "Scheduled task {TaskId} failed attempt={Attempt}; retrying", job.Definition.Id, attempt);
                }
                await Task.Delay(job.Definition.RetryDelay, clock, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            completed.Writer.TryWrite(job);
        }
    }

    private async Task WaitAsync(CancellationToken ct)
    {
        var delay = due.TryPeek(out _, out var next)
            ? TimeSpan.FromTicks(Math.Max(0, next - clock.GetUtcNow().UtcTicks)) : Timeout.InfiniteTimeSpan;
        // Recheck wall-clock changes without creating a separate polling service.
        if (delay > TimeSpan.FromMinutes(1)) delay = TimeSpan.FromMinutes(1);
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timer = Task.Delay(delay, clock, wake.Token);
        var completion = completed.Reader.WaitToReadAsync(wake.Token).AsTask();
        await Task.WhenAny(timer, completion).ConfigureAwait(false);
        await wake.CancelAsync().ConfigureAwait(false);
        try { await Task.WhenAll(timer, completion).ConfigureAwait(false); }
        catch (OperationCanceledException) when (wake.IsCancellationRequested) { }
    }
}
