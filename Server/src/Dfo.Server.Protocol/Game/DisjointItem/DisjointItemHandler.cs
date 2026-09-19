using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DisjointItem;

public sealed class DisjointItemHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService slotMappings,
    ICommandBus commands,
    INotificationBus notifications,
    IGameMessageSender sender) : ClientMessageHandler<DisjointItemRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        DisjointItemRequest message,
        CancellationToken cancellationToken)
    {
        var targetSlot = slotMappings.ToBusinessSlot(message.TargetSlot);
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        EquipmentDisjointResult result;
        if (session is null
            || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
            result = new(EquipmentDisjointRejection.NoSession);
        else
            result = await commands.SendAsync(new DisjointItemCommand(
                online.OnlineUserId,
                session,
                targetSlot,
                message.DisjointItemSlotIndex,
                message.ContextValue,
                EquipmentDisjointSettings.System), cancellationToken).ConfigureAwait(false);

        if (result.Receipt is { } receipt)
            await notifications.PublishAsync(
                new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new EquipmentDisjointCompleted(receipt),
                cancellationToken).ConfigureAwait(false);
        else
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new DisjointItemResponse(null, ErrorCode: ErrorCode(result.Rejection)),
                cancellationToken).ConfigureAwait(false);
    }

    internal static byte ErrorCode(EquipmentDisjointRejection rejection) => rejection switch
    {
        EquipmentDisjointRejection.InventoryFull => 0x04,
        EquipmentDisjointRejection.NoSession
            or EquipmentDisjointRejection.InvalidRequest
            or EquipmentDisjointRejection.ItemUnavailable
            or EquipmentDisjointRejection.CannotDisjoint
            or EquipmentDisjointRejection.UnsupportedMethod => 0x13,
        _ => throw new ArgumentOutOfRangeException(nameof(rejection)),
    };
}
