using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Auction;

/// <summary>A committed auction write: its reply, then the acting character's changed slots and gold.</summary>
public sealed record AuctionCompleted(SelectedSessionIdentity Session, AuctionReply Reply, MailboxAssetProjection Assets)
    : INotification<AuctionCompleted>;

public sealed class AuctionCompletedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<AuctionCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, AuctionCompleted notice,
        CancellationToken token)
    {
        if (recipient.CharacterId != notice.Session.CharacterId || recipient.ConnectionId != notice.Session.ConnectionId
            || !sessions.IsInWorld(notice.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var assets = notice.Assets;
        var frames = new List<IServerNetworkMessage> { notice.Reply };
        frames.AddRange(assets.Updates.GroupBy(x => A21InventoryLayout.ToGameSlot(x.Slot).Space)
            .Select(group => GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(group.Key,
                group.Select(x => new InventoryItemState(x.Identity, A21InventoryLayout.ToGameSlot(x.Slot).Index, x.Core,
                    AvatarDetail: x.AvatarDetail)).ToArray(), assets.PreparedAt))));
        frames.AddRange(assets.Cleared.Select(x =>
        {
            var wire = A21InventoryLayout.ToGameSlot(x);
            return GameInventoryDelivery.ClearedSlotNotification(wire.Space, wire.Index, assets.PreparedAt);
        }));
        if (assets.Gold is { } gold) frames.Add(GameInventoryDelivery.CarriedGoldNotification(gold, assets.PreparedAt));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(frames);
    }
}

/// <summary>What the Auction slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class AuctionRegistration
{
    internal static IServiceCollection AddAuctionSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<AuctionAveragePriceDecoder>(ClientMessageType.GameAuctionAveragePrice);
        services.AddGameCmdMessageDecoder<AuctionRegisterDecoder>(ClientMessageType.GameAuctionRegister);
        services.AddGameCmdMessageDecoder<AuctionCancelDecoder>(ClientMessageType.GameAuctionCancel);
        services.AddGameCmdMessageDecoder<AuctionBidDecoder>(ClientMessageType.GameAuctionBid);
        services.AddGameCmdMessageDecoder<AuctionSearchByItemDecoder>(ClientMessageType.GameAuctionSearchByItem);
        services.AddGameCmdMessageDecoder<AuctionSearchByCriteriaDecoder>(ClientMessageType.GameAuctionSearchByCriteria);
        services.AddGameCmdMessageDecoder<AuctionMyListingsDecoder>(ClientMessageType.GameAuctionMyListings);
        services.AddGameCmdMessageDecoder<AuctionMyBidsDecoder>(ClientMessageType.GameAuctionMyBids);
        services.AddGameCmdMessageDecoder<AuctionBuyPiecesDecoder>(ClientMessageType.GameAuctionBuyPieces);
        services.AddGameCmdMessageEncoder<AuctionAveragePriceResponse, AuctionReplyEncoder<AuctionAveragePriceResponse>>();
        services.AddGameCmdMessageEncoder<AuctionRegisterResponse, AuctionReplyEncoder<AuctionRegisterResponse>>();
        services.AddGameCmdMessageEncoder<AuctionCancelResponse, AuctionReplyEncoder<AuctionCancelResponse>>();
        services.AddGameCmdMessageEncoder<AuctionBidResponse, AuctionReplyEncoder<AuctionBidResponse>>();
        services.AddGameCmdMessageEncoder<AuctionSearchResponse, AuctionReplyEncoder<AuctionSearchResponse>>();
        services.AddGameCmdMessageEncoder<AuctionMyListingsResponse, AuctionReplyEncoder<AuctionMyListingsResponse>>();
        services.AddGameCmdMessageEncoder<AuctionMyBidsResponse, AuctionReplyEncoder<AuctionMyBidsResponse>>();
        services.AddGameCmdMessageEncoder<AuctionBuyPiecesResponse, AuctionReplyEncoder<AuctionBuyPiecesResponse>>();
        services.AddNotificationHandler<AuctionCompleted, AuctionCompletedHandler>();
        services.AddProtocolCommandHandler<GameAuctionCommand, AuctionReply, GameAuctionCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameAuctionAveragePrice, typeof(AuctionAveragePriceRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameAuctionRegister, typeof(AuctionRegisterRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameAuctionCancel, typeof(AuctionCancelRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameAuctionBid, typeof(AuctionBidRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameAuctionSearchByItem, typeof(AuctionSearchRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameAuctionSearchByCriteria, typeof(AuctionSearchRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameAuctionMyListings, typeof(AuctionMineRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameAuctionMyBids, typeof(AuctionMineRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameAuctionBuyPieces, typeof(AuctionBuyPiecesRequest)));
        Handle<AuctionAveragePriceRequest>(services, ClientMessageType.GameAuctionAveragePrice);
        Handle<AuctionRegisterRequest>(services, ClientMessageType.GameAuctionRegister);
        Handle<AuctionCancelRequest>(services, ClientMessageType.GameAuctionCancel);
        Handle<AuctionBidRequest>(services, ClientMessageType.GameAuctionBid);
        Handle<AuctionSearchRequest>(services, ClientMessageType.GameAuctionSearchByItem);
        Handle<AuctionSearchRequest>(services, ClientMessageType.GameAuctionSearchByCriteria);
        Handle<AuctionMineRequest>(services, ClientMessageType.GameAuctionMyListings);
        Handle<AuctionMineRequest>(services, ClientMessageType.GameAuctionMyBids);
        Handle<AuctionBuyPiecesRequest>(services, ClientMessageType.GameAuctionBuyPieces);
        return services;
    }

    private static void Handle<TMessage>(IServiceCollection services, ClientMessageType type) where TMessage : class, IAuctionRequest =>
        services.RegisterProtocolMessageHandler<AuctionRequestHandler, IAuctionRequest, GameAuctionCommand>(type, typeof(TMessage));
}
