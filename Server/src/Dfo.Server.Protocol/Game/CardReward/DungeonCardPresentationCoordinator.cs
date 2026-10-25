using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonClear;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CardReward;

public sealed record DungeonCardPresentationTiming(
    TimeSpan LayoutDelay,
    TimeSpan AutoFlipDelay)
{
    public static DungeonCardPresentationTiming Production { get; } =
        new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
}

public interface IDungeonCardPresentationDelay
{
    ValueTask WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemDungeonCardPresentationDelay(TimeProvider clock)
    : IDungeonCardPresentationDelay
{
    public ValueTask WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        new(Task.Delay(delay, clock, cancellationToken));
}

/// <summary>
/// Drives the disposable presentation clock around authoritative participant settlement and flips.
/// </summary>
/// <remarks>
/// Each delay runs detached from the job that scheduled it; once due it enqueues a timer CMD and
/// waits for it (spec appendix A row 6). As a producer drain it cancels the delays and waits for the
/// timers in flight before the executor stops (RQ-009). Stopping takes no lock (TASK-10 ruling 5).
/// </remarks>
public sealed class DungeonCardPresentationCoordinator(
    ICommandBus commandBus, IDungeonCardPresentationDelay delay,
    ILogger<DungeonCardPresentationCoordinator> logger, DungeonCardPresentationTiming timing)
    : IDungeonCardPresentationControl, IGameplayProducerDrain, IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<ParticipantKey, CancellationTokenSource> pending = [];
    private readonly CancellationTokenSource stopping = new();
    private readonly TaskCompletionSource idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int stopped;
    private int inflight;

    public void ScheduleAfterSettlement(SelectedSessionIdentity session, DungeonRunId runId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonCardPresentationCoordinator) + "." + nameof(ScheduleAfterSettlement));
        Schedule(session, runId, false);
    }
    public void ScheduleAutoFlip(SelectedSessionIdentity session, DungeonRunId runId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonCardPresentationCoordinator) + "." + nameof(ScheduleAutoFlip));
        Schedule(session, runId, true);
    }
    private void Schedule(SelectedSessionIdentity session, DungeonRunId runId, bool auto)
    {
        ArgumentNullException.ThrowIfNull(session);
        var commandType = auto ? "timer:DungeonCardPresentationCoordinator/auto-flip" : "timer:DungeonCardPresentationCoordinator/layout";
        if (TryReplace(session.CharacterId, runId) is not { } cancellation) { NotScheduled(commandType); return; }
        StartDetached(() => RunTimerAsync(session, runId, auto, commandType, cancellation.Token));
    }

    public void Cancel(CharacterId characterId, DungeonRunId runId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonCardPresentationCoordinator) + "." + nameof(Cancel));
        CancellationTokenSource? cancellation = null;
        lock (this.gate)
        {
            _ = this.pending.Remove(new ParticipantKey(runId, characterId), out cancellation);
        }
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    /// <summary>
    /// Returns null once stopped: a request arriving after the drain is treated as one the drain
    /// cancelled, so the order in which the producer drains run does not matter.
    /// </summary>
    private CancellationTokenSource? TryReplace(CharacterId characterId, DungeonRunId runId)
    {
        CancellationTokenSource? prior = null;
        var next = CancellationTokenSource.CreateLinkedTokenSource(this.stopping.Token);
        var refused = false;
        lock (this.gate)
        {
            if (Volatile.Read(ref this.stopped) != 0)
            {
                refused = true;
            }
            else
            {
                var key = new ParticipantKey(runId, characterId);
                _ = this.pending.Remove(key, out prior);
                this.pending.Add(key, next);
            }
        }
        if (refused)
        {
            next.Dispose();
            return null;
        }
        prior?.Cancel();
        prior?.Dispose();
        return next;
    }

    private void NotScheduled(string commandType) =>
        logger.LogInformation(
            "{CommandType} was not scheduled: the card presentation coordinator has stopped",
            commandType);

    private void Complete(
        CharacterId characterId,
        DungeonRunId runId,
        CancellationToken token)
    {
        CancellationTokenSource? completed = null;
        lock (this.gate)
        {
            var key = new ParticipantKey(runId, characterId);
            if (this.pending.TryGetValue(key, out var current)
                && current.Token == token)
            {
                this.pending.Remove(key);
                completed = current;
            }
        }
        completed?.Dispose();
    }

    /// <summary>
    /// Starts a timer under suppressed flow: it must not inherit the scheduling job's token or frames.
    /// </summary>
    private void StartDetached(Func<Task> timer)
    {
        Interlocked.Increment(ref this.inflight);
        // Suppressing an already suppressed flow throws.
        if (ExecutionContext.IsFlowSuppressed())
        {
            _ = timer();
            return;
        }
        using (ExecutionContext.SuppressFlow())
            _ = timer();
    }

    private async Task RunTimerAsync(SelectedSessionIdentity session, DungeonRunId runId, bool auto, string commandType, CancellationToken token)
    {
        try
        {
            await Task.Yield();
            await delay.WaitAsync(auto ? timing.AutoFlipDelay : timing.LayoutDelay, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            ICommand<bool> command = auto ? new DungeonCardAutoFlipCommand(session, runId, token) : new DungeonCardLayoutCommand(session, runId, token);
            await commandBus.SendInternalAsync(command,
                new InternalCommandOrigin(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer),
                    commandType, InternalCommandCancellation.BodyOnly), CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (GameplayExecutorStoppedException stopped)
        {
            logger.Log(stopped.InnerException is null ? LogLevel.Information : LogLevel.Warning, stopped,
                "{CommandType} was not enqueued: the gameplay executor has stopped", commandType);
        }
        catch (Exception error)
        {
            logger.Log(GameplayJob.EndedAJob(error) ? LogLevel.Debug : LogLevel.Error, error,
                "{CommandType} failed for character={CharacterId} run={RunId}", commandType, session.CharacterId.Value, runId.Value);
        }
        finally
        {
            Complete(session.CharacterId, runId, token);
            if (Interlocked.Decrement(ref this.inflight) == 0 && Volatile.Read(ref this.stopped) != 0) this.idle.TrySetResult();
        }
    }

    /// <summary>
    /// RQ-009: no timer enqueues once this returns; those already waiting on their CMD have run it.
    /// </summary>
    public async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        this.Stop();
        if (Volatile.Read(ref this.inflight) == 0)
            this.idle.TrySetResult();
        await this.idle.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels every timer without waiting. The cancelled sources left in <c>pending</c> are not
    /// disposed; the container or process ends right after.
    /// </summary>
    public void Dispose() => this.Stop();

    private void Stop()
    {
        if (Interlocked.Exchange(ref this.stopped, 1) == 0)
            this.stopping.Cancel();
    }

    private readonly record struct ParticipantKey(
        DungeonRunId RunId,
        CharacterId CharacterId);
}
