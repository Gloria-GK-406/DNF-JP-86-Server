using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Auction;

public sealed record GameAuctionCommand(NetworkDispatchContext Dispatch, IAuctionRequest Request) : IProtocolCommand<AuctionReply>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

/// <summary>Every auction request: turns it into one auction house call and answers it.</summary>
/// <remarks>
/// Every request is answered, a malformed or out-of-world one with a code the client shows nothing
/// for. A write that the house refuses is rolled back whole. On success the reply goes first and the
/// committed inventory and gold follow, as for a mail send: the client applies its own change on the
/// reply and the authoritative state then overwrites it.
/// </remarks>
public sealed class GameAuctionCommandHandler(
    ISelectedSessionDomainService sessions,
    IAuctionHouseDomainService house,
    IAuctionQueryDomainService query,
    IMailboxAssetDomainService assets,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    ICommandProjections projections,
    TimeProvider clock,
    ILogger<GameAuctionCommandHandler> logger) : ICommandHandler<GameAuctionCommand, AuctionReply>
{
    private const int SearchBatch = 60;
    private const int MyListingsLimit = 30;
    private const int MyBidsLimit = 60;

    public async ValueTask<AuctionReply> HandleAsync(CommandExecutionContext context, GameAuctionCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var connection = command.Dispatch.Connection;
        var session = sessions.FindByConnection(connection.ConnectionId);
        AuctionReply reply;
        MailboxAssetProjection? projection = null;
        if (session is null || !sessions.IsInWorld(session) || request.Malformed
            || request.PayType > (byte)AuctionMarket.GoldConsignment)
        {
            reply = Refused(request, AuctionReplyCode.Silent);
        }
        else
        {
            var recipient = new SelectedSessionRecipient(session, connection, request.Metadata);
            var market = (AuctionMarket)request.PayType;
            (reply, projection) = request switch
            {
                AuctionSearchRequest search => (await SearchAsync(context, recipient, search, ct).ConfigureAwait(false), null),
                AuctionMineRequest mine => (await MineAsync(context, recipient, mine, market, ct).ConfigureAwait(false), null),
                AuctionAveragePriceRequest prices => (await PricesAsync(context, recipient, prices, market, ct).ConfigureAwait(false), null),
                _ => await WriteAsync(context, recipient, request, market, ct).ConfigureAwait(false),
            };
        }
        logger.LogInformation("Auction {Request} character={Character} market={Market}: {Outcome}",
            request.MessageType, session?.CharacterId.Value, request.PayType, reply.Refusal?.ToString() ?? "ok");
        if (projection is not null && session is not null)
            _ = await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
                new AuctionCompleted(session, reply, projection), ct).ConfigureAwait(false);
        else
            await outbox.RegisterCmdAsync(connection.ConnectionId, reply, ct).ConfigureAwait(false);
        // A consignment purchase moved Cera out of the buyer's account and token Cera into the
        // seller's: both wallets are shown again, the seller's only if the seller is in the world.
        if (session is not null && request is AuctionBidRequest bid
            && reply is AuctionBidResponse { Refusal: null, PayType: (byte)AuctionMarket.GoldConsignment })
        {
            _ = await projections.ProjectAsync(new NotificationAudience.TheseSessions([session]),
                new Dfo.Server.Abstractions.Accounts.AccountWalletChanged(session.AccountId), ct).ConfigureAwait(false);
            var sellers = await units.ReadOnlyAsync(context, new("auction-sold-listing", []),
                async token => await query.FindAsync(bid.Listing, token).ConfigureAwait(false) is { } sold
                    ? new[] { sold.SellerAccount } : [], ct).ConfigureAwait(false);
            if (sellers.Length == 1 && sessions.Find(sellers[0]) is { } seller)
                _ = await projections.ProjectAsync(new NotificationAudience.TheseSessions([seller]),
                    new Dfo.Server.Abstractions.Accounts.AccountWalletChanged(sellers[0]), ct).ConfigureAwait(false);
        }
        return reply;
    }

    private async ValueTask<(AuctionReply, MailboxAssetProjection?)> WriteAsync(CommandExecutionContext context,
        SelectedSessionRecipient recipient, IAuctionRequest request, AuctionMarket market, CancellationToken ct)
    {
        var character = recipient.Session.CharacterId;
        return await units.WriteAsync(context, new("auction-" + request.MessageType, [recipient]), async (unit, token) =>
        {
            try
            {
                var outcome = request switch
                {
                    AuctionRegisterRequest register => await house.ListAsync(character, new(market, market == AuctionMarket.GoldConsignment
                        ? new AuctionLotSource.Gold(register.ItemId)
                        : new AuctionLotSource.CarriedItem(register.Slot, register.ItemId, register.Count), register.Pricing), token).ConfigureAwait(false),
                    AuctionCancelRequest cancel => await house.CancelAsync(character, market, cancel.Listing, token).ConfigureAwait(false),
                    AuctionBidRequest bid => await BidAsync(character, market, bid, token).ConfigureAwait(false),
                    AuctionBuyPiecesRequest pieces => await BuyPiecesAsync(character, pieces, token).ConfigureAwait(false),
                    _ => throw new InvalidOperationException("Not an auction write."),
                };
                var changes = outcome.Receipt?.Changes ?? [];
                var projection = changes.Count == 0 ? null : await assets.PrepareAsync(character, changes, token).ConfigureAwait(false);
                return (Accepted(request), projection);
            }
            catch (AuctionRefusedException refused)
            {
                unit.RequireRollback(refused);
                return (Refused(request, CodeOf(refused.Refusal)), (MailboxAssetProjection?)null);
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>The request names the seller too; a listing of another name is not the one the client saw.</summary>
    private async ValueTask<AuctionOutcome> BidAsync(Dfo.Server.Data.Characters.CharacterId bidder, AuctionMarket market,
        AuctionBidRequest bid, CancellationToken ct)
    {
        if (market == AuctionMarket.Items && (await query.FindAsync(bid.Listing, ct).ConfigureAwait(false) is not { } listing
            || !AuctionPayloadCodec.SameName(bid.SellerName, listing.SellerName)))
            throw new AuctionRefusedException(AuctionRefusal.ListingNotFound);
        return await house.BidAsync(bidder, market, bid.Listing, bid.Amount, ct).ConfigureAwait(false);
    }

    private async ValueTask<AuctionOutcome> BuyPiecesAsync(Dfo.Server.Data.Characters.CharacterId buyer, AuctionBuyPiecesRequest pieces,
        CancellationToken ct)
    {
        if (await query.FindAsync(pieces.Listing, ct).ConfigureAwait(false) is not { } listing
            || listing.ItemId != pieces.ItemId || !AuctionPayloadCodec.SameName(pieces.SellerName, listing.SellerName))
            throw new AuctionRefusedException(AuctionRefusal.ListingNotFound);
        return await house.BuyPiecesAsync(buyer, pieces.Listing, pieces.Count, pieces.Amount, ct).ConfigureAwait(false);
    }

    private async ValueTask<AuctionReply> SearchAsync(CommandExecutionContext context, SelectedSessionRecipient recipient,
        AuctionSearchRequest search, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var page = await units.ReadOnlyAsync(context, new("auction-search", [recipient]),
            token => query.SearchAsync(search.Criteria!, search.Position, SearchBatch, now, token), ct).ConfigureAwait(false);
        return new AuctionSearchResponse(search.PayType, null, page.Total, Views(page.Listings, now));
    }

    private async ValueTask<AuctionReply> MineAsync(CommandExecutionContext context, SelectedSessionRecipient recipient,
        AuctionMineRequest mine, AuctionMarket market, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var character = recipient.Session.CharacterId;
        var listings = await units.ReadOnlyAsync(context, new("auction-mine", [recipient]),
            token => mine.Bids
                ? query.ByTopBidderAsync(character, market, MyBidsLimit, now, token)
                : query.BySellerAsync(character, market, MyListingsLimit, token), ct).ConfigureAwait(false);
        return mine.Bids
            ? new AuctionMyBidsResponse(mine.PayType, null, Views(listings, now))
            : new AuctionMyListingsResponse(mine.PayType, null, Views(listings, now));
    }

    private async ValueTask<AuctionReply> PricesAsync(CommandExecutionContext context, SelectedSessionRecipient recipient,
        AuctionAveragePriceRequest prices, AuctionMarket market, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var reference = await units.ReadOnlyAsync(context, new("auction-prices", [recipient]),
            token => query.PriceReferenceAsync(market, prices.ItemId, now, token), ct).ConfigureAwait(false);
        return new AuctionAveragePriceResponse(prices.PayType, null, reference);
    }

    /// <summary>Hours left as 70 counts them (<c>getExpiringTime</c>): 1 within the last hour, else whole hours plus one.</summary>
    internal static byte HoursLeft(DateTimeOffset expires, DateTimeOffset now)
    {
        var seconds = (long)Math.Max(0, (expires - now).TotalSeconds);
        return (byte)Math.Min(byte.MaxValue, seconds <= 3600 ? 1 : seconds / 3600 + 1);
    }

    private static IReadOnlyList<AuctionListingView> Views(IReadOnlyList<AuctionListing> listings, DateTimeOffset now) =>
        listings.Select(listing => new AuctionListingView(listing, HoursLeft(listing.ExpiresAt, now))).ToArray();

    private static AuctionReply Accepted(IAuctionRequest request) => request switch
    {
        AuctionRegisterRequest r => new AuctionRegisterResponse(r.PayType, null),
        AuctionCancelRequest r => new AuctionCancelResponse(r.PayType, null),
        AuctionBidRequest r => new AuctionBidResponse(r.PayType, null),
        AuctionBuyPiecesRequest => new AuctionBuyPiecesResponse(null),
        _ => throw new InvalidOperationException("Not an auction write."),
    };

    private static AuctionReply Refused(IAuctionRequest request, AuctionReplyCode code)
    {
        var pt = request.PayType > 1 ? (byte)0 : request.PayType;
        return request switch
        {
            AuctionRegisterRequest => new AuctionRegisterResponse(pt, code),
            AuctionCancelRequest => new AuctionCancelResponse(pt, code),
            AuctionBidRequest => new AuctionBidResponse(pt, code),
            AuctionBuyPiecesRequest => new AuctionBuyPiecesResponse(code),
            AuctionAveragePriceRequest => new AuctionAveragePriceResponse(pt, code, null),
            AuctionSearchRequest => new AuctionSearchResponse(pt, code, 0, []),
            AuctionMineRequest { Bids: true } => new AuctionMyBidsResponse(pt, code, []),
            AuctionMineRequest => new AuctionMyListingsResponse(pt, code, []),
            _ => throw new InvalidOperationException("Not an auction request."),
        };
    }

    internal static AuctionReplyCode CodeOf(AuctionRefusal refusal) => refusal switch
    {
        AuctionRefusal.MarketClosed => AuctionReplyCode.Closed,
        AuctionRefusal.ListingNotFound => AuctionReplyCode.NoSuchListing,
        AuctionRefusal.NotSeller => AuctionReplyCode.NoPermission,
        AuctionRefusal.OwnListing => AuctionReplyCode.OwnListing,
        AuctionRefusal.AlreadyTopBidder => AuctionReplyCode.AlreadyTopBidder,
        AuctionRefusal.BidTooLow => AuctionReplyCode.BidTooLow,
        AuctionRefusal.InvalidPrice or AuctionRefusal.NotDivisible => AuctionReplyCode.InvalidPrice,
        AuctionRefusal.NotEligible => AuctionReplyCode.NotListable,
        AuctionRefusal.NeedsCoupon => AuctionReplyCode.NeedsCoupon,
        AuctionRefusal.ItemMoved => AuctionReplyCode.MissingItem,
        AuctionRefusal.ExceedsHolding => AuctionReplyCode.ExceedsHolding,
        AuctionRefusal.ListingLimit => AuctionReplyCode.ListingLimit,
        AuctionRefusal.InsufficientFunds => AuctionReplyCode.InsufficientGold,
        _ => AuctionReplyCode.Silent,
    };
}

public sealed class AuctionRequestHandler(ICommandBus commands) : ClientMessageHandler<IAuctionRequest>, IProtocolCommandMessageHandler<GameAuctionCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, IAuctionRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameAuctionCommand(context, message), ct).ConfigureAwait(false);
}
