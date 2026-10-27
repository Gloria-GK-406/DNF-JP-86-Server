using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CeraShopPurchase;

public sealed record GameCeraShopPurchaseCommand(NetworkDispatchContext Dispatch, CeraShopPurchaseRequest Input)
    : IProtocolCommand<CeraShopPurchaseResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameCeraShopPurchaseCommandHandler(IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions, ICeraShopDomainService shops, TimeProvider clock,
    ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox,
    ILogger<GameCeraShopPurchaseCommandHandler> logger, ICharacterVisibleSessionDomainService visibility)
    : ICommandHandler<GameCeraShopPurchaseCommand, CeraShopPurchaseResult>
{
    public async ValueTask<CeraShopPurchaseResult> HandleAsync(CommandExecutionContext context,
        GameCeraShopPurchaseCommand command, CancellationToken ct)
    {
        var dispatch = command.Dispatch;
        var message = command.Input;
        logger.LogInformation("CERA cart {ConnectionId} payload={Cart}", dispatch.Connection.ConnectionId,
            System.Text.Json.JsonSerializer.Serialize(message.Input));
        var session = sessions.FindByConnection(dispatch.Connection.ConnectionId);
        CeraShopPurchaseResult result;
        if (session is null || !onlineUsers.TryGetByConnection(dispatch.Connection.ConnectionId, out var online) || online is null)
            result = CeraShopPurchaseResult.Refused(CeraShopRejection.NoSession);
        else
        {
            if (context.Request is not CommandRequestContext.ProtocolNonEffect)
                throw new InvalidOperationException("Cera purchase requires its explicit non-effect request.");
            var actor = new SelectedSessionRecipient(session, dispatch.Connection, message.Metadata);
            var outcome = await units.WriteResultAsync(context,
                new("CeraPurchase", [actor], Footprint: CommandFootprintMode.NonRequest),
                async (unit, token) =>
                {
                    var footprint = unit.Footprint;
                    try
                    {
                        return new ActionOutcome(await shops.PurchaseAsync(session, message.Input,
                            clock.GetUtcNow(), footprint, token).ConfigureAwait(false), null);
                    }
                    catch (Exception failure) when (failure is not OperationCanceledException
                        && !TransactionOutcomeUncertainException.Contains(failure))
                    {
                        unit.RequireRollback(failure);
                        return new ActionOutcome(null, failure);
                    }
                }, ct).ConfigureAwait(false);
            result = outcome switch
            {
                CommandWriteResult<ActionOutcome>.Returned { Value: { Failure: { } failure } } => Refused(failure, dispatch),
                CommandWriteResult<ActionOutcome>.Returned { Value: { Result: { } value } } => value,
                CommandWriteResult<ActionOutcome>.CommitRolledBack rollback => Refused(rollback.Cause, dispatch),
                _ => throw new InvalidOperationException("Cera purchase returned no action outcome."),
            };
        }
        if (result.Receipt is { } receipt)
        {
            logger.LogInformation("CERA purchase character={CharacterId} products={Products} cera={Cera} token={Token} happy={Happy} gold={Gold} slots={Slots}",
                receipt.Session.CharacterId.Value, string.Join(',', receipt.ProductIds), receipt.Balances.Cera,
                receipt.Balances.TokenCera, receipt.Balances.HappyTokenCera, receipt.Gold, receipt.Updates.Sum(update => update.Items.Count));
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new CeraShopPurchaseCompleted(receipt), ct).ConfigureAwait(false);
            if (receipt.NameTagChanged)
                await projections.ProjectAsync(new NotificationAudience.TheseSessions(visibility.Resolve(receipt.Session)),
                    new CharacterUserStateRefresh(receipt.Session), ct).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation("CERA purchase {ConnectionId} refused={Reason} detail={Detail}",
                dispatch.Connection.ConnectionId, result.Rejection, result.Detail);
            await outbox.RegisterCmdAsync(dispatch.Connection.ConnectionId,
                new CeraShopPurchaseResponse(null, ErrorCode(result.Rejection)), ct).ConfigureAwait(false);
        }
        return result;
    }

    private CeraShopPurchaseResult Refused(Exception failure, NetworkDispatchContext dispatch)
    {
        if (failure is CeraShopPurchaseRejectedException rejected)
            return CeraShopPurchaseResult.Refused(rejected.Reason, rejected.Message);
        logger.LogError(failure, "CERA purchase {ConnectionId} failed before a successful command result", dispatch.Connection.ConnectionId);
        return CeraShopPurchaseResult.Refused(CeraShopRejection.StorageFailure, failure.Message);
    }

    private static byte ErrorCode(CeraShopRejection reason) => reason switch
    {
        CeraShopRejection.NoRoom => 4,
        CeraShopRejection.InsufficientFunds => 11,
        CeraShopRejection.None => throw new ArgumentOutOfRangeException(nameof(reason)),
        _ => 1,
    };

    private sealed record ActionOutcome(CeraShopPurchaseResult? Result, Exception? Failure);
}
