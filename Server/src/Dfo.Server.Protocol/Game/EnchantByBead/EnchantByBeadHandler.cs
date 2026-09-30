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

public sealed class EnchantByBeadHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService mappings,
    ICommandBus commands,
    INotificationBus notifications,
    IGameMessageSender sender,
    EnchantByBeadReplayGuard replays,
    ILogger<EnchantByBeadHandler> logger) : ClientMessageHandler<EnchantByBeadRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, EnchantByBeadRequest request, CancellationToken ct)
    {
        using var replay = await replays.EnterAsync(
            context.Connection, request.Metadata.Sequence, ct).ConfigureAwait(false);
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            await Reject(context, ct).ConfigureAwait(false);
            return;
        }
        replay.BindSession(session);
        if (replay.CachedFor(request, out var conflict) is { } cached)
        {
            // Refresh from current committed slots, not the old receipt: an intervening inventory
            // command may already have moved or consumed either item.
            await notifications.PublishAsync(
                new NotificationAudience.TheseCharacters([cached.Receipt.Session.CharacterId]),
                new EquipmentEnchantCompleted(cached.Receipt, cached.Response), ct)
                .ConfigureAwait(false);
            return;
        }
        if (conflict || !replay.Admit(request))
        {
            await Reject(context, ct).ConfigureAwait(false);
            return;
        }
        if (request.BeadList != InventoryContainerType.Main || request.BeadSlot < 0)
        {
            await Reject(context, ct, 0x11).ConfigureAwait(false);
            return;
        }
        if (request.TargetList is not (InventoryContainerType.Main or InventoryContainerType.Equipment)
            || request.TargetSlot < 0
            || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
        {
            await Reject(context, ct).ConfigureAwait(false);
            return;
        }

        BusinessSlot bead;
        BusinessSlot target;
        try
        {
            bead = mappings.ToBusinessSlot(new(request.BeadList, request.BeadSlot));
            target = mappings.ToBusinessSlot(new(request.TargetList, request.TargetSlot));
        }
        catch (InventorySlotMappingException)
        {
            await Reject(context, ct, 0x13).ConfigureAwait(false);
            return;
        }
        EquipmentEnchantResult result;
        try
        {
            result = await commands.SendAsync(new EnchantByBeadCommand(
                online.OnlineUserId, session, bead, target), ct).ConfigureAwait(false);
        }
        catch
        {
            replay.MarkCommitFailed(request);
            throw;
        }
        if (result.Receipt is not { } receipt)
        {
            logger.LogDebug("ENCHANT_BY_BEAD rejected: {Reason}", result.Rejection);
            await Reject(context, ct, ErrorCode(result.Rejection)).ConfigureAwait(false);
            return;
        }

        // SendAsync returns after the account/online-user transaction commits.
        var response = new EnchantByBeadResponse(true, TargetList: request.TargetList,
            TargetSlot: request.TargetSlot);
        replay.Remember(request, response, receipt);
        logger.LogInformation("ENCHANT_BY_BEAD character={Character} bead={Bead} target={Target} card={Card}",
            receipt.Session.CharacterId.Value, receipt.BeadSlot, receipt.TargetSlot,
            receipt.CardItemId);
        await notifications.PublishAsync(
            new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
            new EquipmentEnchantCompleted(receipt, response), ct).ConfigureAwait(false);
    }

    private static byte ErrorCode(EquipmentEnchantRejection rejection) => rejection switch
    {
        EquipmentEnchantRejection.MaterialUnavailable => 0x11,
        EquipmentEnchantRejection.UnsupportedContent => 0x17,
        _ => 0x13,
    };

    private ValueTask Reject(NetworkDispatchContext context, CancellationToken ct,
        byte errorCode = 0x13) =>
        sender.SendCmdAsync(context.Connection.ConnectionId,
            new EnchantByBeadResponse(false, errorCode), ct);
}
