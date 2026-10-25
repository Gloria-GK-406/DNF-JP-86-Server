using Dfo.Server.Application.Dispatching;
using Dfo.Server.Abstractions.DomainEvents;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.ShopPurchaseCount;

public sealed record ShopPurchaseCountRequest(GameMessageMetadata Metadata, int NpcId) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameShopPurchaseCount;
}

internal sealed class ShopPurchaseCountDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 4)
            throw new InvalidDataException($"SHOP_PURCHASE_COUNT requires 4 body bytes; received {body.Length}.");
        return new ShopPurchaseCountRequest(metadata, BinaryPrimitives.ReadInt32LittleEndian(body));
    }
}

public sealed class ShopPurchaseCountHandler(ICommandBus commands)
    : ClientMessageHandler<ShopPurchaseCountRequest>, IProtocolCommandMessageHandler<GameShopPurchaseCountCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        ShopPurchaseCountRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameShopPurchaseCountCommand(context, message), cancellationToken).ConfigureAwait(false);
}

public sealed record ShopPurchaseCountsReported(SelectedSessionIdentity Session, NpcShopPurchaseCountsResult Result)
    : INotification<ShopPurchaseCountsReported>;

public sealed class ShopPurchaseCountsReportedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<ShopPurchaseCountsReported>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        ShopPurchaseCountsReported notification, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            recipient.CharacterId == notification.Session.CharacterId
            && recipient.ConnectionId == notification.Session.ConnectionId
            && sessions.IsInWorld(notification.Session)
                ? [new ShopPurchaseCountResponse(notification.Result)] : []);
}
