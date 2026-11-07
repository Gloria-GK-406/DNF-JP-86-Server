using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.UpgradeChronicle;

public sealed record GameUpgradeChronicleCommand(NetworkDispatchContext Dispatch, UpgradeChronicleRequest Input)
    : IProtocolCommand<EquipmentEmancipateResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class UpgradeChronicleHandler(ICommandBus commands)
    : ClientMessageHandler<UpgradeChronicleRequest>, IProtocolCommandMessageHandler<GameUpgradeChronicleCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, UpgradeChronicleRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameUpgradeChronicleCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class GameUpgradeChronicleCommandHandler(
    ISelectedSessionDomainService sessions,
    IEquipmentEmancipateDomainService emancipation, ICommandWorkUnits units, ICommandProjections projections,
    IGameCommandOutbox outbox, ILogger<GameUpgradeChronicleCommandHandler> logger)
    : ICommandHandler<GameUpgradeChronicleCommand, EquipmentEmancipateResult>
{
    public async ValueTask<EquipmentEmancipateResult> HandleAsync(CommandExecutionContext context,
        GameUpgradeChronicleCommand command, CancellationToken ct)
    {
        var requestContext = context.Request ?? throw new InvalidOperationException("Emancipation requires its explicit request.");
        var request = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null || requestContext is CommandRequestContext.ProtocolEffectRejected)
            return await Reject(command, EquipmentEmancipateRejection.NoSession, ct).ConfigureAwait(false);
        BusinessSlot ticket, target;
        try
        {
            ticket = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, request.TicketSlot));
            target = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, request.TargetSlot));
        }
        catch (InventorySlotMappingException)
        {
            return await Reject(command, EquipmentEmancipateRejection.ItemUnavailable, ct).ConfigureAwait(false);
        }
        if (requestContext is not CommandRequestContext.ProtocolEffectSelected selected || session != selected.Recipient.Session)
            throw new InvalidOperationException("Emancipation requires its originally selected actor.");
        var outcome = await units.WriteResultAsync(context,
            new("upgrade-chronicle", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                try
                {
                    return new Outcome(await emancipation.EmancipateAsync(session, ticket, request.TicketItemId,
                        target, request.TargetItemId, unit.Footprint, token).ConfigureAwait(false), null);
                }
                catch (Exception failure) when (failure is not OperationCanceledException
                    && !TransactionOutcomeUncertainException.Contains(failure))
                {
                    unit.RequireRollback(failure);
                    return new Outcome(null, failure);
                }
            }, ct).ConfigureAwait(false);
        var settled = outcome switch
        {
            CommandWriteResult<Outcome>.Returned returned => returned.Value,
            CommandWriteResult<Outcome>.CommitRolledBack rollback => new Outcome(null, rollback.Cause),
            _ => throw new InvalidOperationException("Emancipation returned no outcome."),
        };
        if (settled.Failure is { } systemFailure)
        {
            logger.LogError(systemFailure, "UPGRADE_CHRONICLE failed for character={Character}", session.CharacterId.Value);
            await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,
                new UpgradeChronicleResponse(false, SystemFailureCode), ct).ConfigureAwait(false);
            return new(EquipmentEmancipateRejection.InvalidRequest);
        }
        var result = settled.Result!;
        if (result.Receipt is not { } receipt)
        {
            logger.LogInformation("UPGRADE_CHRONICLE rejected character={Character} ticket={Ticket}@{TicketSlot} target={Target}@{TargetSlot} reason={Reason}",
                session.CharacterId.Value, request.TicketItemId, request.TicketSlot, request.TargetItemId, request.TargetSlot, result.Rejection);
            return await Reject(command, result.Rejection, ct).ConfigureAwait(false);
        }
        logger.LogInformation("UPGRADE_CHRONICLE character={Character} uid={Uid} {From}->{To} opaque={Opaque}",
            session.CharacterId.Value, receipt.After.ItemUid?.Value, receipt.Before.ItemId, receipt.After.ItemId, request.Opaque);
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new EquipmentEmancipateCompleted(receipt), ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>A21 jump-table text "error information"; used when the transaction itself failed.</summary>
    internal const byte SystemFailureCode = 0x01;

    /// <summary>Codes from the A21 failure table 0x0111ACCC/0x0111AC94; unlisted codes would be silent.</summary>
    internal static byte ErrorCode(EquipmentEmancipateRejection rejection) => rejection switch
    {
        EquipmentEmancipateRejection.ItemUnavailable => 0x04,
        EquipmentEmancipateRejection.Durability => 0x07,
        EquipmentEmancipateRejection.InUse => 0x13,
        EquipmentEmancipateRejection.MaterialUnavailable => 0x16,
        EquipmentEmancipateRejection.Expired or EquipmentEmancipateRejection.NotInTown => 0x1B,
        EquipmentEmancipateRejection.Unpurified => 0xAF,
        EquipmentEmancipateRejection.Locked => 0xD6,
        EquipmentEmancipateRejection.NotApplicable => 0x11,
        _ => SystemFailureCode,
    };

    private async ValueTask<EquipmentEmancipateResult> Reject(GameUpgradeChronicleCommand command,
        EquipmentEmancipateRejection rejection, CancellationToken ct)
    {
        await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,
            new UpgradeChronicleResponse(false, ErrorCode(rejection)), ct).ConfigureAwait(false);
        return new(rejection);
    }

    private sealed record Outcome(EquipmentEmancipateResult? Result, Exception? Failure);
}
