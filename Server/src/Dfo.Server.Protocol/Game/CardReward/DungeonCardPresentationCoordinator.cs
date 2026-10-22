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
    ISelectedSessionDomainService selectedSessions,
    IDungeonRunDomainService runs,
    ICommandBus commandBus,
    IGameMessageSender sender,
    DungeonCardSelectionProjectionDelivery projection,
    TimeProvider clock,
    IDungeonCardPresentationDelay delay,
    ILogger<DungeonCardPresentationCoordinator> logger,
    DungeonCardPresentationTiming timing,
    IGameplayQueue queue) : IGameplayProducerDrain, IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<ParticipantKey, CancellationTokenSource> pending = [];
    private readonly CancellationTokenSource stopping = new();
    private readonly TaskCompletionSource idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int stopped;
    private int inflight;

    public void ScheduleAfterSettlement(SelectedSessionIdentity session, DungeonRunId runId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var cancellation = this.Replace(session.CharacterId, runId);
        this.StartDetached(() => this.RunTimerAsync(
            "timer:DungeonCardPresentationCoordinator/layout",
            timing.LayoutDelay,
            () => this.RunLayoutAsync(session, runId, cancellation.Token),
            error => logger.LogError(
                error,
                "Dungeon card layout timer failed for character={CharacterId} run={RunId}",
                session.CharacterId.Value,
                runId.Value),
            cancellation.Token));
    }

    public async ValueTask OpenOnRequestAsync(
        SelectedSessionIdentity session,
        DungeonRunId runId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await this.OpenLayoutAsync(session, runId, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Cancel(CharacterId characterId, DungeonRunId runId)
    {
        CancellationTokenSource? cancellation = null;
        lock (this.gate)
        {
            _ = this.pending.Remove(new ParticipantKey(runId, characterId), out cancellation);
        }
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private CancellationTokenSource Replace(CharacterId characterId, DungeonRunId runId)
    {
        CancellationTokenSource? prior = null;
        var next = CancellationTokenSource.CreateLinkedTokenSource(this.stopping.Token);
        lock (this.gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref this.stopped) != 0, this);
            var key = new ParticipantKey(runId, characterId);
            _ = this.pending.Remove(key, out prior);
            this.pending.Add(key, next);
        }
        prior?.Cancel();
        prior?.Dispose();
        return next;
    }

    private async Task RunLayoutAsync(
        SelectedSessionIdentity session,
        DungeonRunId runId,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await this.OpenLayoutAsync(session, runId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Dungeon card layout timer failed for character={CharacterId} run={RunId}",
                session.CharacterId.Value,
                runId.Value);
        }
        finally
        {
            this.Complete(session.CharacterId, runId, cancellationToken);
        }
    }

    private async ValueTask OpenLayoutAsync(
        SelectedSessionIdentity session,
        DungeonRunId runId,
        CancellationToken cancellationToken)
    {
        if (!this.IsCurrent(session, runId, out var run)
            || run.ClearRewardCards is null
            || !run.IsSettled)
        {
            this.Cancel(session.CharacterId, runId);
            return;
        }

        if (runs.TryOpenCardLayout(session.CharacterId, runId) is null)
            return;
        if (!this.IsCurrent(session, runId, out _))
        {
            this.Cancel(session.CharacterId, runId);
            return;
        }

        await sender.SendExplicitAsync(
            session.ConnectionId,
            new GameExplicitMessageDelivery(
                [
                    new ScoreScrollStateResponse(),
                    new CardSelectRightStateResponse(
                        run.Participants
                            .Where(participant => runs.Find(participant.CharacterId)?.RunId == runId)
                            .Select(participant => participant.Slot)
                            .ToArray()),
                ],
                []),
            cancellationToken).ConfigureAwait(false);
        if (runs.Find(session.CharacterId) is { } afterLayout
            && afterLayout.RunId == runId
            && afterLayout.CardSelections.Count > 0)
        {
            await sender.SendCmdAsync(
                session.ConnectionId,
                new SelectCardResponse(afterLayout.CardSelections),
                cancellationToken).ConfigureAwait(false);
        }

        runs.ScheduleClearReturn(session.CharacterId, runId, clock.GetUtcNow());
        var cancellation = this.Replace(session.CharacterId, runId);
        this.StartDetached(() => this.RunTimerAsync(
            "timer:DungeonCardPresentationCoordinator/auto-flip",
            timing.AutoFlipDelay,
            () => this.RunAutoFlipAsync(session, runId, cancellation.Token),
            error => logger.LogError(
                error,
                "Dungeon card auto flip failed for character={CharacterId} run={RunId}",
                session.CharacterId.Value,
                runId.Value),
            cancellation.Token));
    }

    private async Task RunAutoFlipAsync(
        SelectedSessionIdentity session,
        DungeonRunId runId,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A transient reservation conflict retries the same personal confirmation. It must
            // never turn into a choice of another shared display column.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (!this.IsCurrent(session, runId, out var run)
                    || !run.CardLayoutOpen
                    || run.ClearRewardCards is not { } cards
                    || cards.IsClaimed(ClearRewardCardKind.Free, 0))
                    return;

                var result = await commandBus.SendAsync(
                    new SelectCardCommand(
                        session.AccountId,
                        session.CharacterId,
                        ClearRewardCardKind.Free,
                        0,
                        runId),
                    cancellationToken).ConfigureAwait(false);
                if (result.Status is SelectCardStatus.Flipped && result.Change is not null)
                {
                    await projection.DeliverAsync(
                        session, result, clock.GetUtcNow(), cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }
                if (result.Status is not SelectCardStatus.AlreadyFlipped)
                    return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Dungeon card auto flip failed for character={CharacterId} run={RunId}",
                session.CharacterId.Value,
                runId.Value);
        }
        finally
        {
            this.Complete(session.CharacterId, runId, cancellationToken);
        }
    }

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

    private bool IsCurrent(
        SelectedSessionIdentity session,
        DungeonRunId runId,
        out DungeonRunState run)
    {
        var current = runs.Find(session.CharacterId);
        if (selectedSessions.IsInWorld(session)
            && current is not null
            && current.RunId == runId)
        {
            run = current;
            return true;
        }

        run = null!;
        return false;
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

    /// <summary>
    /// Waits out the delay, then runs <paramref name="body"/> as a timer CMD and waits for it. The
    /// CMD is enqueued without the timer's token: once queued it runs, and the body, seeing a
    /// cancelled token, does nothing.
    /// </summary>
    private async Task RunTimerAsync(
        string commandType,
        TimeSpan wait,
        Func<Task> body,
        Action<Exception> failed,
        CancellationToken cancellationToken)
    {
        try
        {
            // Off the scheduling job's stack first: a delay that completes at once would otherwise
            // enqueue from inside that job, which is a self-wait.
            await Task.Yield();
            await delay.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await queue.RunAsync(
                new GameplayWork(
                    GameplayShardKey.World,
                    GameplayCallChain.NewRoot(GameplayCallSource.Timer),
                    commandType,
                    _ => new ValueTask(body())),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (GameplayExecutorStoppedException)
        {
            // Only a stop that skipped the drain, such as disposing a container without a host.
            logger.LogInformation("{CommandType} was not enqueued: the gameplay executor has stopped", commandType);
        }
        catch (Exception exception)
        {
            failed(exception);
        }
        finally
        {
            if (Interlocked.Decrement(ref this.inflight) == 0 && Volatile.Read(ref this.stopped) != 0)
                this.idle.TrySetResult();
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
