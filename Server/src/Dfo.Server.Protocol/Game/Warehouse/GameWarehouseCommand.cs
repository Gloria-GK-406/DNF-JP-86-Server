using Dfo.Server.Abstractions.Content;
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
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Warehouse;

public sealed record GameWarehouseCommand(NetworkDispatchContext Dispatch, WarehouseRequest Input) : IProtocolCommand<InventoryExpansionResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameWarehouseCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users, IInventoryExpansionDomainService expansion, TimeProvider clock,
    ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox, ILogger<GameWarehouseCommandHandler> logger)
    : ICommandHandler<GameWarehouseCommand, InventoryExpansionResult>
{
    public async ValueTask<InventoryExpansionResult> HandleAsync(CommandExecutionContext context, GameWarehouseCommand command, CancellationToken ct)
    {
        var request = context.Request ?? throw new InvalidOperationException("Warehouse requires its explicit request.");
        var message = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null || !users.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            || online is null || request is CommandRequestContext.ProtocolEffectRejected)
            return await Respond(new(InventoryExpansionRejection.NoSession)).ConfigureAwait(false);
        BusinessSlot? slot = null;
        var mappingRejected = false;
        try
        {
            if (message.Metadata.Type is 0x0198 or 0x01AF)
                slot = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, message.Value));
        }
        catch (InventorySlotMappingException) { mappingRejected = true; }
        if (mappingRejected) return await Respond(new(InventoryExpansionRejection.InvalidRequest)).ConfigureAwait(false);
        if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
            throw new InvalidOperationException("Warehouse requires its originally selected actor.");
        var outcome = await units.WriteResultAsync(context, new("Warehouse", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                try
                {
                    var at = clock.GetUtcNow();
                    var result = message.Metadata.Type switch
                    {
                        0x0131 or 0x0132 => await expansion.UpgradeAccountAsync(session, message.Metadata.Type == 0x0131, at, token).ConfigureAwait(false),
                        0x0133 or 0x0134 => await expansion.TransferGoldAsync(session, message.Metadata.Type == 0x0133, message.Value, at, token).ConfigureAwait(false),
                        0x0198 or 0x01AF when slot is { } itemSlot => await expansion.UseItemAsync(session, itemSlot,
                            message.Metadata.Type == 0x0198 ? InventoryExpansionKind.PersonalCargo : InventoryExpansionKind.Main, at, token).ConfigureAwait(false),
                        _ => new InventoryExpansionResult(InventoryExpansionRejection.InvalidRequest),
                    };
                    return new ActionOutcome(result, null);
                }
                catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
                {
                    unit.RequireRollback(failure);
                    return new ActionOutcome(null, failure);
                }
            }, ct).ConfigureAwait(false);
        return outcome switch
        {
            CommandWriteResult<ActionOutcome>.Returned { Value: { Result: { } result } } => await Respond(result).ConfigureAwait(false),
            CommandWriteResult<ActionOutcome>.Returned { Value: { Failure: { } failure } } => await Respond(MapFailure(failure)).ConfigureAwait(false),
            CommandWriteResult<ActionOutcome>.CommitRolledBack rollback => await Respond(MapFailure(rollback.Cause)).ConfigureAwait(false),
            _ => throw new InvalidOperationException("Warehouse returned no outcome."),
        };

        InventoryExpansionResult MapFailure(Exception failure)
        {
            if (failure is InventorySlotMappingException) return new(InventoryExpansionRejection.InvalidRequest);
            if (failure is InventoryExpansionException expansionFailure) return new(expansionFailure.Rejection);
            logger.LogError(failure, "Warehouse {Type:X4} failed for character {Character}", message.Metadata.Type, session.CharacterId);
            return new(InventoryExpansionRejection.StateChanged);
        }
        async ValueTask<InventoryExpansionResult> Respond(InventoryExpansionResult result)
        {
            if (result.Receipt is { } receipt)
                await projections.ProjectAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                    new WarehouseChanged(message.Metadata.Type, receipt), ct).ConfigureAwait(false);
            else
            {
                logger.LogInformation("Warehouse {Type:X4} refused: {Reason}", message.Metadata.Type, result.Rejection);
                // A21 has no CMD response handler for 0x01AF; capacity is refreshed by NOTI 0x000D.
                if (message.Metadata.Type != 0x01AF)
                    await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,
                        new WarehouseResponse(message.Metadata.Type, false, ErrorCode(message.Metadata.Type, result.Rejection)), ct).ConfigureAwait(false);
            }
            return result;
        }
    }
    internal static byte ErrorCode(ushort type, InventoryExpansionRejection reason) => type switch
    {
        0x0133 => reason == InventoryExpansionRejection.GoldLimit ? (byte)95 : (byte)10,
        0x0134 => reason == InventoryExpansionRejection.GoldLimit ? (byte)94 : (byte)10,
        0x0131 or 0x0132 => reason == InventoryExpansionRejection.InsufficientAssets ? (byte)22 : (byte)19,
        0x0198 => 19,
        _ => 1,
    };
    private sealed record ActionOutcome(InventoryExpansionResult? Result, Exception? Failure);
}
