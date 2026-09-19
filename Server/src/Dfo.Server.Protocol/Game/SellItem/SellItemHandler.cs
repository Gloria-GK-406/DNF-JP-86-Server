using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SellItem;

public sealed class SellItemHandler(IOnlineUserQueryDomainService onlineUsers, ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService slotMappings, ICommandBus commands,
    INotificationBus notifications, IGameMessageSender sender, ILogger<SellItemHandler> logger)
    : ClientMessageHandler<SellItemRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SellItemRequest message,
        CancellationToken cancellationToken)
    {
        var slot = slotMappings.ToBusinessSlot(message.Slot);
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        ItemSaleResult result;
        if (message.FieldSum != (byte)message.Slot.Space + message.Slot.Index + message.Count)
            result = new(ItemSaleRejection.InvalidRequest);
        else if (session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online) || online is null)
            result = new(ItemSaleRejection.NoSession);
        else
        {
            try
            {
                result = await commands.SendAsync(new SellItemCommand(online.OnlineUserId, session,
                    slot, message.Count, message.NpcId),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ItemSaleRejectedException rejected) { result = new(rejected.Reason); }
        }
        if (result.Receipt is { } receipt)
        {
            logger.LogInformation("NPC sale character={CharacterId} npc={NpcId} slot={Slot} item={ItemId} count={Count} gold={Gold}",
                receipt.Session.CharacterId.Value, message.NpcId, receipt.Slot, receipt.ItemId, receipt.Count, receipt.Gold);
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new ItemSaleCompleted(receipt), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation("NPC sale {ConnectionId} npc={NpcId} container={Container} slot={Slot} count={Count} refused={Reason}",
                context.Connection.ConnectionId, message.NpcId, message.Slot.Space, message.Slot.Index, message.Count, result.Rejection);
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new SellItemResponse(null, ErrorCode: ErrorCode(result.Rejection)),
                cancellationToken).ConfigureAwait(false);
        }
    }

    internal static byte ErrorCode(ItemSaleRejection reason) => reason switch
    {
        ItemSaleRejection.Locked => 0xd6,
        ItemSaleRejection.GoldLimit => 22,
        ItemSaleRejection.ItemUnavailable => 17,
        ItemSaleRejection.NoSession or ItemSaleRejection.ShopUnavailable or ItemSaleRejection.InvalidRequest
            or ItemSaleRejection.CannotSell => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}
