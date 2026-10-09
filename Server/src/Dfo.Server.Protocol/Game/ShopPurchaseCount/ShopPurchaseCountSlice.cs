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

public sealed record ShopPurchaseCountResponse(NpcShopPurchaseCountsResult Result) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameShopPurchaseCount;
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

internal sealed class ShopPurchaseCountEncoder : GameCmdMessageEncoder<ShopPurchaseCountResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ShopPurchaseCountResponse message)
    {
        if (message.Result.Rejection != NpcShopRejection.None)
            return OnePayload(GameProtocol.ShopPurchaseCountType, new byte[] { 0, 0 });
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(message.Result.Counts.Count);
        foreach (var count in message.Result.Counts)
        {
            writer.Write(count.ItemId);
            writer.Write(count.Count);
        }
        writer.Write(0); // A21 reads a second counted list; the reference sends this one empty.
        return OnePayload(GameProtocol.ShopPurchaseCountType, stream.ToArray());
    }
}

public sealed record QueryShopPurchaseCountsCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, int NpcId)
    : ICommand<NpcShopPurchaseCountsResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class QueryShopPurchaseCountsCommandHandler(INpcShopDomainService shops, TimeProvider clock)
    : ICommandHandler<QueryShopPurchaseCountsCommand, NpcShopPurchaseCountsResult>
{
    public ValueTask<NpcShopPurchaseCountsResult> HandleAsync(CommandExecutionContext context,
        QueryShopPurchaseCountsCommand command, CancellationToken cancellationToken) =>
        shops.PurchaseCountsAsync(command.Session, command.NpcId, clock.GetUtcNow(), cancellationToken);
}

public sealed class ShopPurchaseCountHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    ICommandBus commands,
    IEventService notifications,
    IGameMessageSender sender) : ClientMessageHandler<ShopPurchaseCountRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, ShopPurchaseCountRequest message, CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
        {
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new ShopPurchaseCountResponse(new(NpcShopRejection.NoSession, [])), cancellationToken).ConfigureAwait(false);
            return;
        }
        var result = await commands.SendAsync(
            new QueryShopPurchaseCountsCommand(online.OnlineUserId, session, message.NpcId), cancellationToken)
            .ConfigureAwait(false);
        await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new ShopPurchaseCountsReported(session, result), cancellationToken).ConfigureAwait(false);
    }
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
            && sessions.Find(notification.Session.AccountId) == notification.Session
                ? [new ShopPurchaseCountResponse(notification.Result)] : []);
}
