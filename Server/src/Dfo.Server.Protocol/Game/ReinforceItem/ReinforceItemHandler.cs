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

public sealed class ReinforceItemHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService slotMappings,
    ICommandBus commands,
    INotificationBus notifications,
    IGameMessageSender sender,
    ReinforceItemReplayGuard replays,
    ILogger<ReinforceItemHandler> logger) : ClientMessageHandler<ReinforceItemRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, ReinforceItemRequest message,
        CancellationToken ct)
    {
        using var replay = await replays.EnterAsync(
            context.Connection, message.Metadata.Sequence, ct).ConfigureAwait(false);
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new ReinforceItemResponse(false, 0x04), ct).ConfigureAwait(false);
            return;
        }
        replay.BindSession(session);
        if (replay.CachedFor(message, out var conflict) is { } previous)
        {
            await sender.SendCmdAsync(context.Connection.ConnectionId, previous, ct)
                .ConfigureAwait(false);
            return;
        }
        if (conflict)
        {
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new ReinforceItemResponse(false, 0x04), ct).ConfigureAwait(false);
            return;
        }

        ReinforceItemResponse response;
        EquipmentReinforcementReceipt? receipt = null;
        if (message.Method != 0)
            response = new(false, 0x17);
        else if (!TrySlots(message, out var target, out var material,
                     out var accountMaterialItemId, out var ticket))
            response = new(false, 0x04);
        else
        {
            if (!onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
                || online is null)
                response = new(false, 0x04);
            else
            {
                var result = await commands.SendAsync(new ReinforceItemCommand(
                    online.OnlineUserId, session, target, message.ExpectedItemId,
                    material, accountMaterialItemId, ticket), ct).ConfigureAwait(false);
                if (result.Receipt is { } committed)
                {
                    receipt = committed;
                    response = ResponseFor(committed, message);
                }
                else response = new(false, ErrorCode(result.Rejection));
            }
        }

        // The command bus has returned only after its unit of work committed. A commit failure
        // throws before this point, so a successful replay entry can never be a rolled-back one.
        if (receipt is not null)
        {
            replay.Remember(message, response);
            logger.LogInformation(
                "UPGRADE_ITEM character={Character} seq={Sequence} slot={Slot} old={Old} new={New} outcome={Outcome}",
                receipt.Session.CharacterId.Value, message.Metadata.Sequence,
                message.TargetSlot, receipt.OldLevel, receipt.NewLevel, receipt.Outcome);
            await notifications.PublishAsync(
                new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new EquipmentReinforcementCompleted(receipt, response), ct).ConfigureAwait(false);
        }
        else await sender.SendCmdAsync(context.Connection.ConnectionId, response, ct)
            .ConfigureAwait(false);
    }

    private bool TrySlots(ReinforceItemRequest message, out BusinessSlot target,
        out BusinessSlot? material, out int? accountMaterialItemId,
        out BusinessSlot? ticket)
    {
        target = default;
        material = null;
        accountMaterialItemId = null;
        ticket = null;
        if (message.TargetSlot < 0 || message.MaterialSlot < -1
            || message.OptionalTicketSlot < -1)
            return false;
        try
        {
            target = slotMappings.ToBusinessSlot(
                new(InventoryContainerType.Main, message.TargetSlot));
            if (message.MaterialSlot is >= 354 and <= 364)
                accountMaterialItemId = AccountMaterialCatalog.Definitions
                    .Single(definition => definition.VirtualSlotIndex == message.MaterialSlot).ItemId;
            else if (message.MaterialSlot >= 0)
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
        new(true, Method: 0,
            MaterialSlot: request.MaterialSlot,
            MaterialRemaining: receipt.MaterialRemaining,
            OptionalTicketSlot: request.OptionalTicketSlot,
            OldLevel: receipt.OldLevel,
            Outcome: receipt.Outcome,
            NewLevel: receipt.NewLevel,
            TargetSlot: checked((short)slotMappings.ToGameSlot(receipt.TargetSlot).Index),
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
