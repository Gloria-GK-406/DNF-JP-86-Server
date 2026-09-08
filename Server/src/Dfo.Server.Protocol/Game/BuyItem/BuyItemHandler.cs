using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.BuyItem;

public sealed class BuyItemHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    ICommandBus commands,
    INotificationBus notifications,
    IGameMessageSender sender,
    ILogger<BuyItemHandler> logger) : ClientMessageHandler<BuyItemRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, BuyItemRequest message, CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        NpcShopPurchaseResult result;
        if (session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
            result = NpcShopPurchaseResult.Refused(NpcShopRejection.NoSession);
        else
        {
            try
            {
                result = await commands.SendAsync(new BuyItemCommand(online.OnlineUserId, session,
                    message.ItemId, message.RawCount, message.ShopId, message.NpcId), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (NpcShopPurchaseRejectedException rejected)
            {
                result = NpcShopPurchaseResult.Refused(rejected.Reason);
            }
        }
        if (result.Receipt is { } receipt)
        {
            logger.LogInformation("NPC purchase character={CharacterId} npc={NpcId} shop={ShopId} item={ItemId} count={Count} gold={Gold}",
                receipt.Session.CharacterId.Value, message.NpcId, message.ShopId, receipt.ItemId, receipt.Count, receipt.Gold);
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new NpcShopPurchaseCompleted(receipt), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation("NPC purchase {ConnectionId} npc={NpcId} shop={ShopId} item={ItemId} rawCount={Count} refused={Reason}",
                context.Connection.ConnectionId, message.NpcId, message.ShopId, message.ItemId, message.RawCount, result.Rejection);
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new BuyItemResponse(null, ErrorCode(result.Rejection)), cancellationToken).ConfigureAwait(false);
        }
    }

    internal static byte ErrorCode(NpcShopRejection rejection) => rejection switch
    {
        NpcShopRejection.NoRoom => 4,
        NpcShopRejection.InsufficientGold => 10,
        NpcShopRejection.InsufficientMaterials => 1,
        NpcShopRejection.PurchaseLimitReached => 0x85,
        NpcShopRejection.ItemNotSold => 17,
        NpcShopRejection.NoSession or NpcShopRejection.ShopUnavailable
            or NpcShopRejection.InvalidCount or NpcShopRejection.UnsupportedItem => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, "A refusal needs a known client error mapping."),
    };
}
