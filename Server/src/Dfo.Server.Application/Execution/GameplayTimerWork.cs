using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Execution;

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
    public static Task TickAsync(CoalescingGameplaySource ticks, string commandType,
        Func<CancellationToken, ValueTask> body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        var run = new Run(commandType, body, cancellationToken, GameplayCallChain.NewRoot(GameplayCallSource.Timer));
        return run.WaitAsync(ticks.TryRun(run.Work, CancellationToken.None).Completion);
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
