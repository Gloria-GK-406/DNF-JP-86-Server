using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.CardReward;
internal sealed partial class GameCardCommandHandler(
    ISelectedSessionDomainService selectedSessions, ISelectedCharacterCurrentViewDomainService currentViews,
    IDungeonRunDomainService runs, ICardFlipDomainService flips,
    IInventorySlotProjectionDomainService inventoryProjection, ICommandWorkUnits units, IGameCommandOutbox outbox,
    IDungeonCardPresentationControl cardPresentation, IDungeonRunProjectionDomainService deliveries,
    IPartyService parties, TimeProvider clock, ILogger<GameCardCommandHandler> logger)
    : ICommandHandler<GameScoreScrollStateCommand, bool>, ICommandHandler<GameCardSelectRightStateCommand, bool>,
      ICommandHandler<GameSelectCardCommand, bool>, ICommandHandler<DungeonCardLayoutCommand, bool>,
      ICommandHandler<DungeonCardAutoFlipCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameScoreScrollStateCommand command, CancellationToken ct)
    { await ScrollAsync(command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameCardSelectRightStateCommand command, CancellationToken ct)
    { await RightAsync(command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSelectCardCommand command, CancellationToken ct)
    { await SelectAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, DungeonCardLayoutCommand command, CancellationToken ct)
    {
        if (command.PendingCancellation.IsCancellationRequested) return true;
        try { await OpenLayoutAsync(command.Session, command.RunId, command.PendingCancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) when (command.PendingCancellation.IsCancellationRequested) { }
        return true;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, DungeonCardAutoFlipCommand command, CancellationToken ct)
    {
        if (command.PendingCancellation.IsCancellationRequested) return true;
        try
        {
            if (!IsCurrent(command.Session, command.RunId, out var run) || !run.CardLayoutOpen
                || run.ClearRewardCards is not { } fixedCards || fixedCards.IsClaimed(ClearRewardCardKind.Free, 0)) return true;
            // The executor cannot advance another reservation between attempts in this job.
            var result = await AwardAsync(execution, command.Session.AccountId, command.Session.CharacterId,
                ClearRewardCardKind.Free, 0, command.RunId, command.PendingCancellation).ConfigureAwait(false);
            if (result.Status == SelectCardStatus.Flipped && result.Change is not null)
                await ProjectAsync(command.Session, result, clock.GetUtcNow(), command.PendingCancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (command.PendingCancellation.IsCancellationRequested) { }
        return true;
    }
    private ISelectedSessionDomainService Sessions => selectedSessions;
    private IDungeonRunDomainService Runs => runs;
    private bool Admitted(SelectedSessionIdentity session, DungeonRunId runId) =>
        DungeonPartyRecipients.FindCurrent(parties, selectedSessions, runs, deliveries, session, runId).Contains(session);
    private bool IsCurrent(SelectedSessionIdentity session, DungeonRunId runId, out DungeonRunState run)
    {
        var current = runs.Find(session.CharacterId);
        if (selectedSessions.IsInWorld(session) && current is not null && current.RunId == runId) { run = current; return true; }
        run = null!; return false;
    }
    internal async ValueTask OpenLayoutAsync(SelectedSessionIdentity session, DungeonRunId runId, CancellationToken cancellationToken)
    {
        if (!this.IsCurrent(session, runId, out var run)
            || run.ClearRewardCards is null
            || !run.IsSettled)
        {
            cardPresentation.Cancel(session.CharacterId, runId);
            return;
        }

        if (runs.TryOpenCardLayout(session.CharacterId, runId) is null)
            return;
        if (!this.IsCurrent(session, runId, out _))
        {
            cardPresentation.Cancel(session.CharacterId, runId);
            return;
        }

        await outbox.RegisterExplicitAsync(
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
            await outbox.RegisterCmdAsync(
                session.ConnectionId,
                new SelectCardResponse(afterLayout.CardSelections),
                cancellationToken).ConfigureAwait(false);
        }

        runs.ScheduleClearReturn(session.CharacterId, runId, clock.GetUtcNow());
        cardPresentation.ScheduleAutoFlip(session, runId);
    }
    private async ValueTask SelectAsync(CommandExecutionContext execution, NetworkDispatchContext context, SelectCardRequest message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);

        // The published session as well as the identity, because a paid flip is priced by the
        // level the character stands at and the level is one of the things a published session
        // carries. Reading it here is a read of state this handler does not own and does not
        // change; what is done with it is the command's.
        var published = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || published is null)
        {
            logger.LogWarning(
                "Select card {ConnectionId}: no published session, ignoring {Kind} position {Index}",
                context.Connection.ConnectionId,
                message.Kind,
                message.Index);
            return;
        }

        var run = runs.Find(session.CharacterId);
        if (run is null || !selectedSessions.IsInWorld(session))
            return;
        if (!Admitted(session, run.RunId))
            return;

        var result = await AwardAsync(execution, session.AccountId, session.CharacterId, message.Kind,
            message.Kind is ClearRewardCardKind.Free ? 0 : message.Index, run.RunId, cancellationToken).ConfigureAwait(false);

        if (result.Status is not SelectCardStatus.Flipped)
        {
            logger.LogInformation(
                "Select card {ConnectionId} character={CharacterId}: {Kind} position {Index} "
                    + "handed nothing over, {Status}",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                message.Kind,
                message.Index,
                result.Status);
            return;
        }

        logger.LogInformation(
            "Select card {ConnectionId} character={CharacterId}: {Kind} wire position {Index} "
                + "confirmed, party reveal ready={RevealReady}, bag {Bag}, charged {Charged}, paid {Paid}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            message.Kind,
            message.Index,
            result.Change?.Selections.Count > 0,
            result.Delivered is null ? "unchanged" : "told",
            result.GoldCharged,
            result.GoldPaid);

        if (message.Kind is ClearRewardCardKind.Free)
            cardPresentation.Cancel(session.CharacterId, run.RunId);

        await ProjectAsync(session, result, published.World.PreparedAt, cancellationToken).ConfigureAwait(false);
    }
    private async ValueTask ProjectAsync(SelectedSessionIdentity session, SelectCardResult result, DateTimeOffset at, CancellationToken ct)
    {
        var delivery = DungeonCardSelectionProjection.CreateDelivery(selectedSessions, runs, DungeonPartyRecipients.FindCurrent(parties, selectedSessions, runs, deliveries, session, result.Change!.RunId), session, result, at, out _);
        if (delivery is not null) await outbox.RegisterExplicitAsync(session.ConnectionId, delivery, ct).ConfigureAwait(false);
    }
    private async ValueTask ScrollAsync(NetworkDispatchContext context, ScoreScrollStateRequest message, CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);

        if (session is not null
            && runs.Find(session.CharacterId) is { Layout.Despair: null } run)
        {
            logger.LogInformation(
                "Score scroll state {ConnectionId} character={CharacterId}: "
                    + "routed through card presentation",
                context.Connection.ConnectionId,
                session.CharacterId.Value);
            await OpenLayoutAsync(
                session, run.RunId, cancellationToken).ConfigureAwait(false);
            return;
        }

        logger.LogInformation("Score scroll state {ConnectionId} character={CharacterId}: acknowledged",
            context.Connection.ConnectionId, session?.CharacterId.Value);
        IReadOnlyList<IGameServerMessage> frames = [new ScoreScrollStateResponse()];
        await outbox.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(frames, []),
            cancellationToken).ConfigureAwait(false);
    }
    private async ValueTask RightAsync(NetworkDispatchContext context, CardSelectRightStateRequest message, CancellationToken ct)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is not null && runs.Find(session.CharacterId) is { } run)
        { await OpenLayoutAsync(session, run.RunId, ct).ConfigureAwait(false); return; }
        logger.LogInformation("Card layout {ConnectionId} character={CharacterId}: layout sent", context.Connection.ConnectionId, session?.CharacterId.Value);
        await outbox.RegisterCmdAsync(context.Connection.ConnectionId, new CardSelectRightStateResponse(), ct).ConfigureAwait(false);
    }
}
