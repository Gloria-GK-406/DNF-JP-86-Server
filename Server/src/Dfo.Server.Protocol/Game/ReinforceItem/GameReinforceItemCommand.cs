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

namespace Dfo.Server.Protocol.Game.ReinforceItem;

public sealed record GameReinforceItemCommand(NetworkDispatchContext Dispatch, ReinforceItemRequest Input) : IProtocolCommand<EquipmentReinforcementResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameReinforceItemCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService slotMappings,
    IEquipmentReinforcementDomainService reinforcement, ICommandWorkUnits units, ICommandProjections projections,
    IGameCommandOutbox outbox, ILogger<GameReinforceItemCommandHandler> logger)
    : ICommandHandler<GameReinforceItemCommand, EquipmentReinforcementResult>
{
    public async ValueTask<EquipmentReinforcementResult> HandleAsync(CommandExecutionContext context, GameReinforceItemCommand command, CancellationToken ct)
    {
        var requestContext = context.Request ?? throw new InvalidOperationException("Equipment requires its explicit request.");
        var message = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null || requestContext is CommandRequestContext.ProtocolEffectRejected)
        {
            await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,
                new ReinforceItemResponse(false, 0x04), ct).ConfigureAwait(false);
            return new(EquipmentReinforcementRejection.NoSession);
        }
        var result = new EquipmentReinforcementResult(EquipmentReinforcementRejection.InvalidRequest);
        ReinforceItemResponse response;
        EquipmentReinforcementReceipt? receipt = null;
        if (message.Method is not (0 or 1))
            response = new(false, 0x17);
        else if (!TrySlots(message, out var target, out var material, out var ticket))
            response = new(false, 0x04);
        else
        {
            if (!onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
                || online is null)
                response = new(false, 0x04);
            else
            {
                if (requestContext is not CommandRequestContext.ProtocolEffectSelected selected || session != selected.Recipient.Session)
                    throw new InvalidOperationException("Reinforcement requires its originally selected actor.");
                result = await units.WriteAsync(context,
                    new("reinforce-item", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                    (unit, token) => reinforcement.ReinforceAsync(session, target, message.ExpectedItemId,
                        material, ticket, unit.Footprint, token, (EquipmentUpgradeKind)message.Method), ct).ConfigureAwait(false);
                if (result.Receipt is { } committed)
                {
                    receipt = committed;
                    response = ResponseFor(committed, message);
                }
                else response = new(false, ErrorCode(result.Rejection));
            }
        }

        // The pipeline records the primary settlement before these notifications.
        if (receipt is not null)
        {
            logger.LogInformation(
                "UPGRADE_ITEM method={Method} character={Character} seq={Sequence} slot={Slot} old={Old} new={New} outcome={Outcome}",
                message.Method, receipt.Session.CharacterId.Value, message.Metadata.Sequence,
                message.TargetSlot, receipt.OldLevel, receipt.NewLevel, receipt.Outcome);
            await projections.ProjectAsync(
                new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new EquipmentReinforcementCompleted(receipt, response), ct).ConfigureAwait(false);
        }
        else await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId, response, ct)
            .ConfigureAwait(false);
        return result;
    }

    private bool TrySlots(ReinforceItemRequest message, out BusinessSlot target,
        out BusinessSlot? material,
        out BusinessSlot? ticket)
    {
        target = default;
        material = null;
        ticket = null;
        if (message.TargetList is not (InventoryContainerType.Main or InventoryContainerType.Equipment)
            || message.TargetSlot < 0 || message.MaterialSlot < -1
            || message.OptionalTicketSlot < -1)
            return false;
        try
        {
            target = slotMappings.ToBusinessSlot(
                new(message.TargetList, message.TargetSlot));
            if (message.MaterialSlot >= 0)
                material = slotMappings.ToBusinessSlot(
                    new(InventoryContainerType.Main, message.MaterialSlot));
            if (message.OptionalTicketSlot >= 0)
                ticket = slotMappings.ToBusinessSlot(
                    new(InventoryContainerType.Main, message.OptionalTicketSlot));
            return true;
        }
        catch (InventorySlotMappingException) { return false; }
    }

    private ReinforceItemResponse ResponseFor(
        EquipmentReinforcementReceipt receipt, ReinforceItemRequest request) =>
        new(true, Method: checked((byte)request.Method),
            MaterialSlot: request.MaterialSlot,
            MaterialRemaining: receipt.MaterialRemaining,
            OptionalTicketSlot: request.OptionalTicketSlot,
            OldLevel: receipt.OldLevel,
            Outcome: receipt.Outcome,
            NewLevel: receipt.NewLevel,
            TargetSlot: checked((short)slotMappings.ToGameSlot(receipt.TargetSlot).Index),
            TargetList: slotMappings.ToGameSlot(receipt.TargetSlot).Space,
            DestroyRewards: receipt.DestroyRewards.Select(reward =>
                new ReinforceItemReward(slotMappings.ToGameSlot(reward.Slot),
                    reward.ItemId, reward.Count)).ToArray());

    internal static byte ErrorCode(EquipmentReinforcementRejection rejection) => rejection switch
    {
        EquipmentReinforcementRejection.NoRoom => 0x15,
        EquipmentReinforcementRejection.InsufficientGold => 0x0A,
        EquipmentReinforcementRejection.InsufficientMaterial => 0x16,
        EquipmentReinforcementRejection.Locked => 0xD6,
        EquipmentReinforcementRejection.Durability => 0x07,
        EquipmentReinforcementRejection.UnsupportedContent
            or EquipmentReinforcementRejection.UnsupportedOutcome => 0x0D,
        EquipmentReinforcementRejection.CannotReinforce => 0x13,
        EquipmentReinforcementRejection.NoSession
            or EquipmentReinforcementRejection.InvalidRequest
            or EquipmentReinforcementRejection.ItemUnavailable
            or EquipmentReinforcementRejection.WrongItem => 0x04,
        _ => throw new ArgumentOutOfRangeException(nameof(rejection)),
    };
}
