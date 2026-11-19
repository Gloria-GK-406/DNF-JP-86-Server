using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal sealed record GameAntonRaidRewardCommand(NetworkDispatchContext Dispatch, AntonRaidRewardRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal sealed record QueryAntonRaidRewardDeadlines : ICommand<IReadOnlyList<AntonRaidRewardDeadline>>;
internal sealed record AdvanceAntonRaidRewardDeadline(AntonRaidRewardDeadline Deadline) : ICommand<bool>;

internal sealed class AntonRaidRewardMessageHandler(ICommandBus commands) : ClientMessageHandler<AntonRaidRewardRequest>, IProtocolCommandMessageHandler<GameAntonRaidRewardCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, AntonRaidRewardRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameAntonRaidRewardCommand(context, message), ct).ConfigureAwait(false);
}

internal sealed class AntonRaidRewardCommandHandler(IAntonRaidRewardSettlementDomainService settlements,
    IAntonRaidRewardPhaseBridge bridge, ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService users, IAntonRaidRewardGrantDomainService rewards, ICommandWorkUnits units,
    AntonRaidRewardProjection projection, IGameCommandOutbox outbox, TimeProvider clock,
    ILogger<AntonRaidRewardCommandHandler> logger)
    : ICommandHandler<GameAntonRaidRewardCommand, bool>,
      ICommandHandler<QueryAntonRaidRewardDeadlines, IReadOnlyList<AntonRaidRewardDeadline>>,
      ICommandHandler<AdvanceAntonRaidRewardDeadline, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameAntonRaidRewardCommand command, CancellationToken ct)
    {
        execution.Request!.DemandCurrent();
        var connection = command.Dispatch.Connection.ConnectionId;
        if (!sessions.TryGetInWorld(connection, out var session))
            return false;

        var input = command.Input;
        var location = bridge.Locate(session);
        var now = clock.GetUtcNow();
        if (input.MessageType == ClientMessageType.GameFinishAntonRaidMovie)
        {
            var movie = location is { } phase
                ? settlements.FinishMovie(phase.Instance, phase.Phase, session.CharacterId, input.Value, now)
                : null;
            await outbox.RegisterCmdAsync(connection, new AntonRaidReply(ServerMessageType.GameFinishAntonRaidMovieReply, movie is not null), ct)
                .ConfigureAwait(false);
            if (movie is not null && input.Value == 1 && movie.Snapshot.Stage >= AntonRaidRewardStage.Cards)
                await projection.OpenAsync(movie.Snapshot, movie.OpenedNow ? null : session, ct).ConfigureAwait(false);
            return movie is not null;
        }

        var selection = location is { } selectionPhase
            ? settlements.Select(selectionPhase.Instance, selectionPhase.Phase, session.CharacterId, input.Value, input.Slot, now)
            : new AntonRaidRewardSelection(false);
        var award = selection.Grant is { } grant
            ? await GrantAsync(execution, selection.Snapshot!, grant, ct).ConfigureAwait(false)
            : new GrantOutcome(true);
        if (award.Succeeded && selection.Snapshot is { } snapshot)
        {
            var card = selection.Reveal ?? (award.Receipt?.Claimed == true
                ? snapshot.Revealed.Single(opened => opened.Character == session.CharacterId && (byte)opened.Kind == input.Value)
                : null);
            if (card is not null)
                await projection.RevealAsync(snapshot, [card], allRaid: false, ct).ConfigureAwait(false);
        }
        var accepted = selection.Accepted && award.Succeeded;
        await outbox.RegisterCmdAsync(connection, new AntonRaidReply(ServerMessageType.GameSelectAntonRaidRewardReply, accepted, Error: 0x12), ct)
            .ConfigureAwait(false);
        await ProjectGrantAsync(award, ct).ConfigureAwait(false);
        return accepted;
    }

    public ValueTask<IReadOnlyList<AntonRaidRewardDeadline>> HandleAsync(CommandExecutionContext execution,
        QueryAntonRaidRewardDeadlines command, CancellationToken ct) => ValueTask.FromResult(settlements.Deadlines());

    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, AdvanceAntonRaidRewardDeadline command, CancellationToken ct)
    {
        if (settlements.Advance(command.Deadline, clock.GetUtcNow()) is not { } advance)
            return false;

        switch (advance.Kind)
        {
            case AntonRaidRewardAdvanceKind.MoviePrompt:
                await projection.PromptAsync(advance.Snapshot, ct).ConfigureAwait(false);
                break;

            case AntonRaidRewardAdvanceKind.CardsOpened:
                await projection.OpenAsync(advance.Snapshot, only: null, ct).ConfigureAwait(false);
                break;

            case AntonRaidRewardAdvanceKind.CardsAutoSelected:
                var opened = new List<GrantOutcome>();
                foreach (var grant in advance.Grants)
                    opened.Add(await GrantAsync(execution, advance.Snapshot, grant, ct).ConfigureAwait(false));
                await projection.RevealAsync(advance.Snapshot, advance.Revealed, allRaid: false, ct).ConfigureAwait(false);
                foreach (var award in opened)
                    await ProjectGrantAsync(award, ct).ConfigureAwait(false);
                break;

            case AntonRaidRewardAdvanceKind.SquadItemsShown:
                await projection.RevealAsync(advance.Snapshot, advance.Revealed, allRaid: true, ct).ConfigureAwait(false);
                break;

            case AntonRaidRewardAdvanceKind.Deliver:
                var finished = true;
                foreach (var grant in advance.Grants)
                {
                    var award = await GrantAsync(execution, advance.Snapshot, grant, ct).ConfigureAwait(false);
                    finished &= award.Succeeded;
                    await ProjectGrantAsync(award, ct).ConfigureAwait(false);
                }

                if (finished && await bridge.CompleteAsync(execution, advance.Snapshot, ct).ConfigureAwait(false)
                    && settlements.Complete(advance.Snapshot.Seed.Instance, advance.Snapshot.Seed.Phase))
                {
                    settlements.Forget(advance.Snapshot.Seed.Instance);
                }
                break;
        }
        return true;
    }

    private async ValueTask<GrantOutcome> GrantAsync(CommandExecutionContext execution, AntonRaidRewardSnapshot snapshot,
        AntonRaidRewardGrant grant, CancellationToken ct)
    {
        var session = projection.Online(snapshot, grant.Recipient.Character);
        var recipients = session is not null ? TimerRecipientCapture.Capture([session], users) : [];
        Exception? attempted = null;
        var result = await units.WriteResultAsync(execution, new("anton-raid.settle-recipient", recipients),
            async (unit, token) =>
            {
                try
                {
                    var present = session is not null && projection.Online(snapshot, grant.Recipient.Character) == session;
                    return await rewards.GrantAsync(grant, present, token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is not OperationCanceledException
                    && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(error))
                {
                    attempted = error;
                    unit.RequireRollback(error);
                    return new AntonRaidRewardReceipt(false);
                }
            }, ct).ConfigureAwait(false);
        if (result is CommandWriteResult<AntonRaidRewardReceipt>.CommitRolledBack rolledBack)
            attempted = rolledBack.Cause;

        if (attempted is not null)
        {
            logger.LogWarning(attempted, "Anton raid reward {Kind} will retry for {Character} in {Instance} phase {Phase}",
                grant.Kind, grant.Recipient.Character, grant.Instance, grant.Phase);
            return new(false);
        }
        return result is CommandWriteResult<AntonRaidRewardReceipt>.Returned committed
            ? new(true, session, committed.Value)
            : new(false);
    }

    private ValueTask ProjectGrantAsync(GrantOutcome award, CancellationToken ct) =>
        award.Session is { } session && award.Receipt is { } receipt
            ? projection.AssetsAsync(session, receipt, ct) : ValueTask.CompletedTask;

    private sealed record GrantOutcome(bool Succeeded, SelectedSessionIdentity? Session = null,
        AntonRaidRewardReceipt? Receipt = null);
}

internal sealed class AntonRaidRewardDeadlineSource(TimeProvider clock) : DungeonDeadlineSource
{
    public override DungeonDeadlineKind Kind => DungeonDeadlineKind.AntonRaidRewards;

    public override async ValueTask<DateTimeOffset?> RunAsync(DungeonDeadlineRound round, CancellationToken ct)
    {
        var deadlines = await round.SubmitAsync(new QueryAntonRaidRewardDeadlines(), ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        foreach (var deadline in deadlines.TakeWhile(deadline => deadline.At <= now))
            await round.ItemAsync(new AdvanceAntonRaidRewardDeadline(deadline), "advance", ct).ConfigureAwait(false);

        var pending = await round.SubmitAsync(new QueryAntonRaidRewardDeadlines(), ct).ConfigureAwait(false);
        return pending.Count > 0 ? pending[0].At : null;
    }
}
