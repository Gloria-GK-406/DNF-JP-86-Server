using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CeraShopPurchase;

public sealed class CeraShopPurchaseHandler(IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions, ICommandBus commands, INotificationBus notifications,
    IGameMessageSender sender, ILogger<CeraShopPurchaseHandler> logger) : ClientMessageHandler<CeraShopPurchaseRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        CeraShopPurchaseRequest message, CancellationToken cancellationToken)
    {
        logger.LogInformation("CERA cart {ConnectionId} payload={Cart}", context.Connection.ConnectionId,
            System.Text.Json.JsonSerializer.Serialize(message.Input));
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        CeraShopPurchaseResult result;
        if (session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online) || online is null)
            result = CeraShopPurchaseResult.Refused(CeraShopRejection.NoSession);
        else
        {
            try
            {
                result = await commands.SendAsync(new CeraShopPurchaseCommand(online.OnlineUserId, session, message.Input),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (CeraShopPurchaseRejectedException failure)
            { result = CeraShopPurchaseResult.Refused(failure.Reason, failure.Message); }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                logger.LogError(failure, "CERA purchase {ConnectionId} failed before a successful command result", context.Connection.ConnectionId);
                result = CeraShopPurchaseResult.Refused(CeraShopRejection.StorageFailure, failure.Message);
            }
        }
        if (result.Receipt is { } receipt)
        {
            logger.LogInformation("CERA purchase character={CharacterId} products={Products} cera={Cera} token={Token} happy={Happy} gold={Gold} slots={Slots}",
                receipt.Session.CharacterId.Value, string.Join(',', receipt.ProductIds), receipt.Balances.Cera,
                receipt.Balances.TokenCera, receipt.Balances.HappyTokenCera, receipt.Gold, receipt.Updates.Sum(update => update.Items.Count));
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new CeraShopPurchaseCompleted(receipt), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation("CERA purchase {ConnectionId} refused={Reason} detail={Detail}",
                context.Connection.ConnectionId, result.Rejection, result.Detail);
            await sender.SendCmdAsync(context.Connection.ConnectionId, new CeraShopPurchaseResponse(null, ErrorCode(result.Rejection)),
                cancellationToken).ConfigureAwait(false);
        }
    }

    internal static byte ErrorCode(CeraShopRejection reason) => reason switch
    {
        CeraShopRejection.NoRoom => 4,
        CeraShopRejection.InsufficientFunds => 11,
        CeraShopRejection.None => throw new ArgumentOutOfRangeException(nameof(reason)),
        _ => 1,
    };
}
