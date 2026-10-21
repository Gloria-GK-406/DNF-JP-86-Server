using System.Diagnostics;
using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Execution;

/// <summary>
/// The gameplay executor (RQ-001): an unbounded FIFO with one consumer that runs each job to
/// completion before taking the next. The consumer loop starts on the first enqueue and there is no
/// path that runs work inline instead, so nothing depends on hosted-service start order.
/// </summary>
/// <remarks>
/// Logging failures are swallowed everywhere: a broken log provider must neither change a CMD's
/// outcome nor stop the loop (RQ-004).
/// </remarks>
public sealed class GameplayExecutor(ILogger<GameplayExecutor> logger) : GameplayJobHost, IAsyncDisposable, IDisposable
{
    // Not SingleReader: a disposal that times out drains the queue itself while the loop is stuck.
    private readonly System.Threading.Channels.Channel<Job> queue =
        System.Threading.Channels.Channel.CreateUnbounded<Job>(
            new System.Threading.Channels.UnboundedChannelOptions { AllowSynchronousContinuations = false });
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool abandoned;
    private int started;

    /// <summary>How long disposal waits for the queue to drain before abandoning what is left.</summary>
    internal TimeSpan DisposeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public bool IsStarted => Volatile.Read(ref started) != 0;

    /// <summary>Completes when the consumer loop has ended.</summary>
    public Task Completion => completion.Task;

    public ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!work.Chain.IsInitialized)
            return ValueTask.FromException<T>(new ArgumentException("Gameplay work needs a call chain.", nameof(work)));
        if (string.IsNullOrWhiteSpace(work.CommandType))
            return ValueTask.FromException<T>(new ArgumentException("Gameplay work needs a CMD type.", nameof(work)));

        var running = JobRunningAnywhere;
        if (work.Chain.Depth > GameplayCallDepth.Limit)
        {
            Log(LogLevel.Error, null,
                "Gameplay {CommandType} exceeds the depth limit {Limit} and was not enqueued: root {RootId}, " +
                "parent {ParentId}, depth {Depth}, source {Source}; chain {Chain}",
                work.CommandType, GameplayCallDepth.Limit, work.Chain.RootId, work.Chain.ParentId,
                work.Chain.Depth, work.Chain.Source, work.Chain.Describe(work.CommandType));
            Exception exceeded = new GameplayDepthExceededException(work.Chain, work.CommandType);
            return ValueTask.FromException<T>(running is null ? exceeded : Fault(running, exceeded));
        }

        // Only work of this executor waits on itself; another executor is just another queue.
        if (JobRunningHere is { } here)
        {
            Log(LogLevel.Error, null,
                "Gameplay {CommandType} was enqueued from the executor and refused: root {RootId}, parent {ParentId}, " +
                "depth {Depth}, source {Source}; chain {Chain}; enqueued by {Running}",
                work.CommandType, work.Chain.RootId, work.Chain.ParentId, work.Chain.Depth, work.Chain.Source,
                work.Chain.Describe(work.CommandType), ChainOf(here).Describe());
            return ValueTask.FromException<T>(Fault(here,
                new GameplaySelfWaitException($"Waiting for gameplay work {work.CommandType}", ChainOf(here))));
        }

        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<T>(cancellationToken);

        var job = new Job<T>(this, work, cancellationToken, Stopwatch.GetTimestamp());
        job.ArmCancellation();
        if (!queue.Writer.TryWrite(job))
        {
            job.DisarmCancellation();
            return ValueTask.FromException<T>(new GameplayExecutorStoppedException(work.CommandType));
        }

        EnsureStarted();
        return new ValueTask<T>(job.Completion);
    }

    public ValueTask RunAsync(GameplayWork work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        var body = work.Body;
        var typed = new GameplayWork<bool>(work.Key, work.Chain, work.CommandType, async ct =>
        {
            await body(ct).ConfigureAwait(false);
            return true;
        });
        return new ValueTask(RunAsync(typed, cancellationToken).AsTask());
    }

    /// <summary>RQ-009: refuse new work, run everything already queued, then end the loop.</summary>
    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfRunningHere("Stopping the gameplay executor");
        queue.Writer.TryComplete();
        EnsureStarted();
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        ThrowIfRunningHere("Disposing the gameplay executor");
        queue.Writer.TryComplete();
        EnsureStarted();
        try
        {
            await completion.Task.WaitAsync(DisposeTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A job that never ends cannot be interrupted, and the loop is stuck behind it, so the
            // disposer refuses whatever is still queued itself.
            abandoned = true;
            AbandonQueued(cause: null);
            Log(LogLevel.Warning, null, "The gameplay executor did not drain within {Timeout}; queued work is abandoned",
                DisposeTimeout);
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private void EnsureStarted()
    {
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0) return;
        // The loop must not carry the first producer's context into every job.
        if (ExecutionContext.IsFlowSuppressed())
        {
            _ = Task.Run(ConsumeAsync);
            return;
        }
        using (ExecutionContext.SuppressFlow())
            _ = Task.Run(ConsumeAsync);
    }

    private async Task ConsumeAsync()
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (queue.Reader.TryRead(out var job))
                {
                    if (abandoned)
                    {
                        job.Abandon(cause: null);
                        continue;
                    }
                    await job.RunAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception)
        {
            // Jobs contain their own failures, so this is a defect in the loop; fail loudly rather than
            // leave every queued and future caller waiting on a loop that is gone.
            queue.Writer.TryComplete(exception);
            Log(LogLevel.Critical, exception, "The gameplay executor loop faulted; queued work is abandoned");
            AbandonQueued(exception);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private void AbandonQueued(Exception? cause)
    {
        while (queue.Reader.TryRead(out var job)) job.Abandon(cause);
    }

    private bool Enabled(LogLevel level)
    {
        try
        {
            return logger.IsEnabled(level);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Log(LogLevel level, Exception? exception, string message, params object?[] args)
    {
        try
        {
            if (logger.IsEnabled(level)) logger.Log(level, exception, message, args);
        }
        catch (Exception)
        {
            // See remarks: logging never decides an outcome.
        }
    }

    private abstract class Job
    {
        public abstract ValueTask RunAsync();

        public abstract void Abandon(Exception? cause);
    }

    private sealed class Job<T>(
        GameplayExecutor executor,
        GameplayWork<T> work,
        CancellationToken cancellationToken,
        long enqueuedAt) : Job
    {
        private const int Pending = 0;
        private const int Running = 1;
        private const int Cancelled = 2;

        private readonly TaskCompletionSource<T> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationToken callerToken = cancellationToken;
        private CancellationTokenRegistration registration;
        private int state;

        public Task<T> Completion => result.Task;

        public void ArmCancellation()
        {
            if (!callerToken.CanBeCanceled) return;
            registration = callerToken.Register(static self =>
            {
                var job = (Job<T>)self!;
                if (Interlocked.CompareExchange(ref job.state, Cancelled, Pending) == Pending)
                    job.result.TrySetCanceled(job.callerToken);
            }, this);
        }

        public void DisarmCancellation() => registration.Dispose();

        public override void Abandon(Exception? cause)
        {
            if (Interlocked.CompareExchange(ref state, Cancelled, Pending) == Pending)
                result.TrySetException(new GameplayExecutorStoppedException(work.CommandType, cause));
            registration.Dispose();
        }

        public override async ValueTask RunAsync()
        {
            if (Interlocked.CompareExchange(ref state, Running, Pending) != Pending)
            {
                registration.Dispose();
                return;
            }
            registration.Dispose();

            var queueWait = Stopwatch.GetElapsedTime(enqueuedAt).TotalMilliseconds;
            var chain = work.Chain;
            GameplayJobToken? token = null;
            T value = default!;
            Exception? failure = null;
            var cancelled = false;
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                chain = chain.ForCommand(work.CommandType);
                if (executor.Enabled(LogLevel.Trace))
                    executor.Log(LogLevel.Trace, null,
                        "Gameplay {CommandType} started: root {RootId}, parent {ParentId}, depth {Depth}, source {Source}, " +
                        "queued {QueueWaitMilliseconds} ms",
                        work.CommandType, chain.RootId, chain.ParentId, chain.Depth, chain.Source, queueWait);
                token = executor.EnterJob(chain);
                startedAt = Stopwatch.GetTimestamp();
                value = await work.Body(callerToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
            {
                cancelled = true;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (token is not null) ExitJob(token);
            }

            // RQ-003: a program error raised inside the job fails it even if the body swallowed it.
            if (token is not null && ProgramErrorOf(token) is { } programError)
            {
                failure = programError;
                cancelled = false;
            }

            var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            if (failure is not null)
            {
                executor.Log(LogLevel.Error, failure,
                    "Gameplay {CommandType} failed: root {RootId}, parent {ParentId}, depth {Depth}, source {Source}, " +
                    "chain {Chain}, after {ElapsedMilliseconds} ms, {QueueWaitMilliseconds} ms queued",
                    work.CommandType, chain.RootId, chain.ParentId, chain.Depth, chain.Source, chain.Describe(),
                    elapsed, queueWait);
                result.TrySetException(failure);
            }
            else if (cancelled)
            {
                if (executor.Enabled(LogLevel.Debug))
                    executor.Log(LogLevel.Debug, null,
                        "Gameplay {CommandType} cancelled by its caller: root {RootId}, parent {ParentId}, " +
                        "depth {Depth}, source {Source}, after {ElapsedMilliseconds} ms, {QueueWaitMilliseconds} ms queued",
                        work.CommandType, chain.RootId, chain.ParentId, chain.Depth, chain.Source, elapsed, queueWait);
                result.TrySetCanceled(callerToken);
            }
            else
            {
                if (executor.Enabled(LogLevel.Debug))
                    executor.Log(LogLevel.Debug, null,
                        "Gameplay {CommandType} completed: root {RootId}, parent {ParentId}, depth {Depth}, " +
                        "source {Source}, in {ElapsedMilliseconds} ms after {QueueWaitMilliseconds} ms queued",
                        work.CommandType, chain.RootId, chain.ParentId, chain.Depth, chain.Source, elapsed, queueWait);
                result.TrySetResult(value);
            }
        }
    }
}
