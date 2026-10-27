using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Dispatching;

namespace Dfo.Server.Protocol.Game.LotteryOpening;

public sealed record GameLotteryOpeningCommand(NetworkDispatchContext Dispatch, LotteryOpeningRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameLotteryOpeningCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ILotteryOpeningDomainService opening, Dfo.Server.Abstractions.Accounts.IPremiumServiceDomainService premiums, Dfo.Server.Abstractions.Accounts.IPremiumServiceUsageDomainService usage, TimeProvider clock, ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox,
    ILogger<GameLotteryOpeningCommandHandler> logger) : ICommandHandler<GameLotteryOpeningCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameLotteryOpeningCommand command, CancellationToken ct)
    {
        var request = context.Request ?? throw new InvalidOperationException("Opening requires its explicit request.");
        var message = command.Input;
        var connection = command.Dispatch.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null || request is CommandRequestContext.ProtocolEffectRejected || !sessions.IsInWorld(session)
            || !users.TryGetByConnection(connection, out var user) || user is null) return false;
        if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
            throw new InvalidOperationException("Opening requires its originally selected actor.");
        var outcome = await units.WriteResultAsync(context, new("LotteryOpening", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                var footprint = unit.Footprint;
                try
                {
                    return new ActionOutcome(await opening.ExecuteAsync(session, OpeningWireSlots.FromMain(message.Slot), message.Mode, footprint, token).ConfigureAwait(false), null);
                }
                catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
                {
                    unit.RequireRollback(failure);
                    return new ActionOutcome(null, failure);
                }
            }, ct).ConfigureAwait(false);
        var action = outcome switch
        {
            CommandWriteResult<ActionOutcome>.Returned returned => returned.Value,
            CommandWriteResult<ActionOutcome>.CommitRolledBack rollback => new ActionOutcome(null, rollback.Cause),
            _ => throw new InvalidOperationException("Opening returned no action outcome."),
        };
        var result = action.Result;
        byte error = 0;
        if (action.Failure is { } failure)
        {
            if (failure is OpeningRejectedException rejected)
            {
                error = rejected.Reason switch { OpeningRejection.PremiumUnavailable => 0x73, OpeningRejection.NoRoom => 0x7E, _ => (byte)0 };
                logger.LogInformation("Lottery rejected character={Character} slot={Slot}: {Reason}", session.CharacterId, message.Slot, rejected.Message);
            }
            else logger.LogError(failure, "Lottery transaction failed character={Character}", session.CharacterId);
        }
        logger.LogInformation("Lottery character={Character} mode={Mode} seq={Sequence} slot={Slot} committed={Committed} reward={Reward} quantity={Quantity} error={Error}",
            session.CharacterId, message.Mode, message.Metadata.Sequence, message.Slot, result is not null, result?.Receipt.Rewards[0].ItemId, result?.Receipt.Rewards.Sum(x => x.Count), error);
        var response = new LotteryOpeningResponse(result, error);
        if (result is null) await outbox.RegisterCmdAsync(connection, response, ct).ConfigureAwait(false);
        else
        {
            IReadOnlyList<IServerNetworkMessage> following = [];
            if (message.Mode == 1)
            {
                var snapshot = await units.ReadOnlyAsync(context, new("LotteryPremiumSnapshot", [selected.Recipient]), async token =>
                {
                    if (!sessions.IsInWorld(session)) return new PremiumSnapshotResult(new([]), [], clock.GetUtcNow(), false);
                    return new PremiumSnapshotResult(new(await premiums.For(session.AccountId).ReadAsync(token).ConfigureAwait(false)),
                        await usage.ReadAsync(session.CharacterId, token).ConfigureAwait(false), clock.GetUtcNow());
                }, ct).ConfigureAwait(false);
                following = [new PremiumServiceStateMessage(snapshot.Entitlements, snapshot.Usage, snapshot.PreparedAt)];
            }
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]), new OpeningCompleted(result.Receipt, [response], result.GoldAfter, following), ct).ConfigureAwait(false);
        }
        return result is not null;
    }
    private sealed record ActionOutcome(LotteryOpeningResult? Result, Exception? Failure);
}
