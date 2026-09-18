using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
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
public sealed class DungeonCardPresentationCoordinator(
    ISelectedSessionDomainService selectedSessions,
    IDungeonRunDomainService runs,
    ICommandBus commandBus,
    IGameMessageSender sender,
    DungeonCardSelectionProjectionDelivery projection,
    TimeProvider clock,
    IDungeonCardPresentationDelay delay,
    ILogger<DungeonCardPresentationCoordinator> logger,
    DungeonCardPresentationTiming timing) : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<ParticipantKey, CancellationTokenSource> pending = [];
    private bool disposed;

    public void ScheduleAfterSettlement(SelectedSessionIdentity session, DungeonRunId runId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var cancellation = this.Replace(session.CharacterId, runId);
        _ = this.RunLayoutAsync(session, runId, cancellation.Token);
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
        var next = new CancellationTokenSource();
        lock (this.gate)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
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
            await delay.WaitAsync(timing.LayoutDelay, cancellationToken).ConfigureAwait(false);
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

        await sender.SendCmdAsync(
            session.ConnectionId,
            new CardSelectRightStateResponse(
                run.Participants
                    .Where(participant => runs.Find(participant.CharacterId)?.RunId == runId)
                    .Select(participant => participant.Slot)
                    .ToArray()),
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

        var cancellation = this.Replace(session.CharacterId, runId);
        _ = this.RunAutoFlipAsync(session, runId, cancellation.Token);
    }

    private async Task RunAutoFlipAsync(
        SelectedSessionIdentity session,
        DungeonRunId runId,
        CancellationToken cancellationToken)
    {
        try
        {
            await delay.WaitAsync(timing.AutoFlipDelay, cancellationToken).ConfigureAwait(false);
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
        if (selectedSessions.FindByCharacter(session.CharacterId) == session
            && current is not null
            && current.RunId == runId)
        {
            run = current;
            return true;
        }

        run = null!;
        return false;
    }

    public void Dispose()
    {
        CancellationTokenSource[] cancellations;
        lock (this.gate)
        {
            if (this.disposed)
                return;
            this.disposed = true;
            cancellations = [.. this.pending.Values];
            this.pending.Clear();
        }

        foreach (var cancellation in cancellations)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    private readonly record struct ParticipantKey(
        DungeonRunId RunId,
        CharacterId CharacterId);
}
