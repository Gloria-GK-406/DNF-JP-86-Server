using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Forging;

public sealed class ForgingHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService mappings,
    ICommandBus commands,
    INotificationBus notifications,
    IGameMessageSender sender,
    ILogger<ForgingHandler> logger) : ClientMessageHandler<ForgingRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        ForgingRequest request, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogDebug("FORGING no selected session");
            await Reject(context, ct, 0x04).ConfigureAwait(false);
            return;
        }
        // Variant 1/2 requires a separate ticket rule; never assume the ordinary PVF material path.
        if (request.Variant != 0)
        {
            await Reject(context, ct, 0x0D).ConfigureAwait(false);
            return;
        }
        if (request.TargetListType is not ((byte)InventoryContainerType.Main)
                and not ((byte)InventoryContainerType.Equipment)
            || request.TargetSlot < 0 || request.MaterialSlot < 0
            || request.TargetItemId <= 0 || request.ReportedNameBytes.Length is < 1 or > 128
            || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
        {
            await Reject(context, ct, 0x04).ConfigureAwait(false);
            return;
        }
        BusinessSlot target;
        BusinessSlot material;
        try
        {
            target = mappings.ToBusinessSlot(new(
                (InventoryContainerType)request.TargetListType, request.TargetSlot));
            material = mappings.ToBusinessSlot(new(InventoryContainerType.Main,
                request.MaterialSlot));
        }
        catch (InventorySlotMappingException)
        {
            await Reject(context, ct, 0x04).ConfigureAwait(false);
            return;
        }
        var result = await commands.SendAsync(new ForgingCommand(online.OnlineUserId,
            session, target, request.TargetItemId, material), ct).ConfigureAwait(false);
        if (result.Receipt is not { } receipt)
        {
            var errorCode = ErrorCode(result.Rejection);
            await Reject(context, ct, errorCode).ConfigureAwait(false);
            return;
        }
        logger.LogInformation("FORGING character={Character} target={Target} old={Old} new={New} roll={Roll}",
            receipt.Session.CharacterId.Value, request.TargetSlot, receipt.OldLevel,
            receipt.NewLevel, receipt.Roll);
        await notifications.PublishAsync(
            new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
            new ForgingCompleted(receipt), ct).ConfigureAwait(false);
    }

    internal static byte ErrorCode(EquipmentForgingRejection rejection) => rejection switch
    {
        EquipmentForgingRejection.Durability => 0x07,
        EquipmentForgingRejection.UnsupportedContent => 0x0D,
        EquipmentForgingRejection.NotWeapon => 0x13,
        EquipmentForgingRejection.MaterialUnavailable => 0x16,
        EquipmentForgingRejection.MaxLevel => 0x5F,
        EquipmentForgingRejection.Locked => 0xD5,
        _ => 0x04,
    };

    private ValueTask Reject(NetworkDispatchContext context, CancellationToken ct,
        byte errorCode) => sender.SendCmdAsync(context.Connection.ConnectionId,
            new ForgingResponse(false, errorCode), ct);
}
