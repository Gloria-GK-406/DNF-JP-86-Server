using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Execution;

/// <summary>
/// One producer run as a CMD on the executor: a timer source (spec appendix A rows 5-7, RQ-010), a
/// startup recovery, or the consumption of one domain event delivery (row 8, RQ-041). The run is
/// enqueued with <see cref="CancellationToken.None"/>, so a run already queued when the host stops
/// still runs before the executor stops (RQ-009); its body gets the producer's own token, as before.
/// </summary>
/// <remarks>
/// A cancellation the body raises through that token is carried out of the job and rethrown to the
/// producer, which handles it where it always has; the executor does not report it as a failure.
/// There is deliberately no path that runs the body when the executor is absent.
/// </remarks>
public static class GameplayTimerWork
{
    /// <summary>Enqueues one run directly: an entry method, or a startup recovery.</summary>
    public static Task RunAsync(IGameplayQueue queue, string commandType,
        Func<CancellationToken, ValueTask> body, CancellationToken cancellationToken,
        GameplayCallSource source = GameplayCallSource.Timer) =>
        RunAsync(queue, commandType, body, cancellationToken, GameplayCallChain.NewRoot(source));

    /// <summary>Enqueues one run as <paramref name="chain"/>: an event CMD that is its publisher's child (RQ-050).</summary>
    public static Task RunAsync(IGameplayQueue queue, string commandType,
        Func<CancellationToken, ValueTask> body, CancellationToken cancellationToken, GameplayCallChain chain)
    {
        ArgumentNullException.ThrowIfNull(queue);
        var run = new Run(commandType, body, cancellationToken, chain);
        return run.WaitAsync(queue.RunAsync(run.Work, CancellationToken.None).AsTask());
    }

    /// <summary>One tick of a periodic or wake-up loop, through the loop's own source (RQ-006).</summary>
    /// <remarks>
    /// A tick whose CMD failed is absorbed, so the loop goes on to its next tick: the failure may be a
    /// program error that stuck to the job after the body swallowed it (RQ-003), and a hosted service
    /// that ends takes the host down with it. The executor has logged it with its chain; this logs one
    /// Warning without the exception (F-30). A refusal and the caller's cancellation are still thrown.
    /// </remarks>
    public static async Task TickAsync(CoalescingGameplaySource ticks, string commandType,
        Func<CancellationToken, ValueTask> body, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        ArgumentNullException.ThrowIfNull(logger);
        var run = new Run(commandType, body, cancellationToken, GameplayCallChain.NewRoot(GameplayCallSource.Timer));
        try
        {
            await run.WaitAsync(ticks.TryRun(run.Work, CancellationToken.None).Completion).ConfigureAwait(false);
        }
        catch (Exception error) when (GameplayJob.EndedAJob(error))
        {
            logger.LogWarning("Timer {CommandType} failed; the hosted service continues with its next tick", commandType);
        }
    }

    private sealed class Run
    {
        private ExceptionDispatchInfo? cancelled;

        public Run(string commandType, Func<CancellationToken, ValueTask> body, CancellationToken token,
            GameplayCallChain chain)
        {
            ArgumentNullException.ThrowIfNull(body);
            Work = new GameplayWork(GameplayShardKey.World, chain, commandType, async _ =>
            {
                try
                {
                    await body(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException error) when (token.IsCancellationRequested)
                {
                    cancelled = ExceptionDispatchInfo.Capture(error);
                }
            });
        }

        public GameplayWork Work { get; }

        public async Task WaitAsync(Task completion)
        {
            await completion.ConfigureAwait(false);
            cancelled?.Throw();
        }
    }
}
