using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.EnchantByBead;

public sealed record GameEnchantByBeadCommand(NetworkDispatchContext Dispatch, EnchantByBeadRequest Input) : IProtocolCommand<EquipmentEnchantResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameEnchantByBeadCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IEquipmentEnchantDomainService enchant, ICommandWorkUnits units, ICommandProjections projections,
    IGameCommandOutbox outbox, ILogger<GameEnchantByBeadCommandHandler> logger)
    : ICommandHandler<GameEnchantByBeadCommand, EquipmentEnchantResult>
{
    public async ValueTask<EquipmentEnchantResult> HandleAsync(CommandExecutionContext context, GameEnchantByBeadCommand command, CancellationToken ct)
    {
        var requestContext = context.Request ?? throw new InvalidOperationException("Equipment requires its explicit request.");
        var request = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null || requestContext is CommandRequestContext.ProtocolEffectRejected)
        {
            await Reject(command.Dispatch, ct).ConfigureAwait(false);
            return new(EquipmentEnchantRejection.NoSession);
        }
        if (request.BeadList != InventoryContainerType.Main || request.BeadSlot < 0)
        {
            await Reject(command.Dispatch, ct, 0x11).ConfigureAwait(false);
            return new(EquipmentEnchantRejection.InvalidRequest);
        }
        if (request.TargetList is not (InventoryContainerType.Main or InventoryContainerType.Equipment)
            || request.TargetSlot < 0
            || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            || online is null)
        {
            await Reject(command.Dispatch, ct).ConfigureAwait(false);
            return new(EquipmentEnchantRejection.InvalidRequest);
        }

        BusinessSlot bead;
        BusinessSlot target;
        try
        {
            bead = A21InventoryLayout.ToBusinessSlot(new(request.BeadList, request.BeadSlot));
            target = A21InventoryLayout.ToBusinessSlot(new(request.TargetList, request.TargetSlot));
        }
        catch (InventorySlotMappingException)
        {
            await Reject(command.Dispatch, ct, 0x13).ConfigureAwait(false);
            return new(EquipmentEnchantRejection.InvalidRequest);
        }
        if (requestContext is not CommandRequestContext.ProtocolEffectSelected selected || session != selected.Recipient.Session)
            throw new InvalidOperationException("Enchant requires its originally selected actor.");
        var result = await units.WriteAsync(context,
            new("enchant-by-bead", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            (unit, token) => enchant.EnchantAsync(session, bead, target, unit.Footprint, token), ct).ConfigureAwait(false);
        if (result.Receipt is not { } receipt)
        {
            logger.LogDebug("ENCHANT_BY_BEAD rejected: {Reason}", result.Rejection);
            await Reject(command.Dispatch, ct, ErrorCode(result.Rejection)).ConfigureAwait(false);
            return result;
        }

        // The Main unit has completed its account/online-user transaction.
        var response = new EnchantByBeadResponse(true, TargetList: request.TargetList,
            TargetSlot: request.TargetSlot);
        logger.LogInformation("ENCHANT_BY_BEAD character={Character} bead={Bead} target={Target} card={Card}",
            receipt.Session.CharacterId.Value, receipt.BeadSlot, receipt.TargetSlot,
            receipt.CardItemId);
        await projections.ProjectAsync(
            new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
            new EquipmentEnchantCompleted(receipt, response), ct).ConfigureAwait(false);
        return result;
    }

    private static byte ErrorCode(EquipmentEnchantRejection rejection) => rejection switch
    {
        EquipmentEnchantRejection.MaterialUnavailable => 0x11,
        EquipmentEnchantRejection.UnsupportedContent => 0x17,
        _ => 0x13,
    };

    private ValueTask Reject(NetworkDispatchContext context, CancellationToken ct,
        byte errorCode = 0x13) =>
        outbox.RegisterCmdAsync(context.Connection.ConnectionId,
            new EnchantByBeadResponse(false, errorCode), ct);
}
