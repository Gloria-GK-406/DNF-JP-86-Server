using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>What a timer source enqueued, and what its job saw when it ran.</summary>
internal sealed record TimerJobSeen(
    string CommandType,
    GameplayCallSource Source,
    bool OnExecutor,
    GameplayCallChain? Current,
    bool Completed);

/// <summary>
/// Wraps the container's queue: records every enqueue and, from inside the job, where the body ran.
/// </summary>
internal sealed class RecordingGameplayQueue(IGameplayQueue inner) : IGameplayQueue
{
    private int outstanding;
    private int maximumOutstanding;

    public ConcurrentQueue<(string CommandType, GameplayCallSource Source)> Enqueued { get; } = new();

    public ConcurrentQueue<TimerJobSeen> Ran { get; } = new();

    /// <summary>The most CMDs of <see cref="Watched"/> type that were enqueued and not yet complete at once.</summary>
    public int MaximumOutstanding => Volatile.Read(ref maximumOutstanding);

    /// <summary>The CMD type whose outstanding count is tracked.</summary>
    public string? Watched { get; set; }

    public int EnqueuedCount(string commandType) => Enqueued.Count(x => x.CommandType == commandType);

    public ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken cancellationToken)
    {
        Record(work.CommandType, work.Chain.Source);
        var body = work.Body;
        return Track(work.CommandType, inner.RunAsync(work with
        {
            Body = async ct =>
            {
                var seen = (GameplayJob.IsOnExecutor, GameplayJob.Current);
                try
                {
                    var value = await body(ct).ConfigureAwait(false);
                    Ran.Enqueue(new(work.CommandType, work.Chain.Source, seen.IsOnExecutor, seen.Current, true));
                    return value;
                }
                catch
                {
                    Ran.Enqueue(new(work.CommandType, work.Chain.Source, seen.IsOnExecutor, seen.Current, false));
                    throw;
                }
            },
        }, cancellationToken));
    }

    public ValueTask RunAsync(GameplayWork work, CancellationToken cancellationToken)
    {
        var body = work.Body;
        return new ValueTask(RunAsync(new GameplayWork<bool>(work.Key, work.Chain, work.CommandType, async ct =>
        {
            await body(ct).ConfigureAwait(false);
            return true;
        }), cancellationToken).AsTask());
    }

    public async Task<bool> WaitForAsync(Func<TimerJobSeen, bool> match, TimeSpan patience)
    {
        using var timeout = new CancellationTokenSource(patience);
        while (!Ran.Any(match))
        {
            try { await Task.Delay(5, timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }
        }
        return true;
    }

    private void Record(string commandType, GameplayCallSource source)
    {
        Enqueued.Enqueue((commandType, source));
        if (commandType != Watched) return;
        var now = Interlocked.Increment(ref outstanding);
        int seen;
        while (now > (seen = Volatile.Read(ref maximumOutstanding))
            && Interlocked.CompareExchange(ref maximumOutstanding, now, seen) != seen) { }
    }

    private ValueTask<T> Track<T>(string commandType, ValueTask<T> pending)
    {
        if (commandType != Watched) return pending;
        return new ValueTask<T>(ReleaseAfter(pending.AsTask()));

        async Task<T> ReleaseAfter(Task<T> task)
        {
            try { return await task.ConfigureAwait(false); }
            finally { Interlocked.Decrement(ref outstanding); }
        }
    }
}

/// <summary>Holds the executor with one job until released, so later work is observably queued.</summary>
internal sealed class ExecutorBlocker
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Job { get; private set; } = Task.CompletedTask;

    public async Task HoldAsync(IGameplayQueue queue, TimeSpan patience)
    {
        Job = queue.RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection), "test:blocker", async _ =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
            }), CancellationToken.None).AsTask();
        await entered.Task.WaitAsync(patience).ConfigureAwait(false);
    }

    public async Task ReleaseAsync(TimeSpan patience)
    {
        release.TrySetResult();
        await Job.WaitAsync(patience).ConfigureAwait(false);
    }
}
