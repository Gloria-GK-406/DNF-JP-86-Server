using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Auction;

/// <summary>The <c>auction_listings</c> and <c>auction_trades</c> tables.</summary>
/// <remarks>
/// Every move out of <see cref="AuctionListingState.Active"/> is guarded by the row still being
/// active, so a listing reaches one final state once. Times are stored as Unix seconds.
/// </remarks>
internal sealed class SqliteAuctionDomainStore(SqliteDatabase database) : IAuctionDomainStore
{
    private const string Columns = """
        listing_id,market,state,seller_character_id,seller_account_id,seller_name,item_id,quantity_initial,
        quantity_remaining,gold_amount,start_price,buyout_price,unit_price,current_bid,top_bidder_character_id,
        top_bidder_account_id,deposit,fee_rate_bp,created_at,expires_at,display_core
        """;

    public async ValueTask<AuctionListingId> InsertAsync(AuctionListing listing, CancellationToken cancellationToken)
    {
        long id = 0;
        await database.WriteAsync(async (command, token) =>
        {
            command.CommandText = """
                INSERT INTO auction_listings(market,state,seller_character_id,seller_account_id,seller_name,item_id,
                    quantity_initial,quantity_remaining,gold_amount,start_price,buyout_price,unit_price,deposit,fee_rate_bp,
                    created_at,expires_at,display_core)
                VALUES(@market,0,@seller,@account,@name,@item,@quantity,@quantity,@gold,@start,@buyout,@unit,@deposit,@fee,
                    @created,@expires,@core)
                RETURNING listing_id;
                """;
            var p = command.Parameters;
            p.AddWithValue("@market", (int)listing.Market);
            p.AddWithValue("@seller", listing.Seller.Value);
            p.AddWithValue("@account", listing.SellerAccount.Value);
            p.AddWithValue("@name", listing.SellerName);
            p.AddWithValue("@item", listing.ItemId);
            p.AddWithValue("@quantity", listing.QuantityInitial);
            p.AddWithValue("@gold", listing.GoldAmount);
            p.AddWithValue("@start", (object?)listing.Pricing.StartPrice ?? DBNull.Value);
            p.AddWithValue("@buyout", (object?)listing.Pricing.BuyoutPrice ?? DBNull.Value);
            p.AddWithValue("@unit", (object?)listing.Pricing.UnitPrice ?? DBNull.Value);
            p.AddWithValue("@deposit", listing.Deposit);
            p.AddWithValue("@fee", listing.FeeRateBasisPoints);
            p.AddWithValue("@created", listing.CreatedAt.ToUnixTimeSeconds());
            p.AddWithValue("@expires", listing.ExpiresAt.ToUnixTimeSeconds());
            p.AddWithValue("@core", listing.DisplayCore is { } core ? InventoryItemCore.Encode(core) : DBNull.Value);
            id = Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
        }, cancellationToken).ConfigureAwait(false);
        return new AuctionListingId(id);
    }

    public ValueTask<AuctionListing?> FindAsync(AuctionListingId listing, CancellationToken cancellationToken) =>
        database.ReadFirstAsync($"SELECT {Columns} FROM auction_listings WHERE listing_id=@id",
            p => p.AddWithValue("@id", listing.Value), Read, cancellationToken);

    public async ValueTask<int> CountActiveAsync(CharacterId seller, AuctionMarket market, CancellationToken cancellationToken) =>
        (await database.ReadAllAsync(
            "SELECT COUNT(*) FROM auction_listings WHERE seller_character_id=@seller AND market=@market AND state=0",
            p => { p.AddWithValue("@seller", seller.Value); p.AddWithValue("@market", (int)market); },
            r => r.GetInt32(0), cancellationToken).ConfigureAwait(false))[0];

    public async ValueTask<bool> SetBidAsync(AuctionListingId listing, long amount, CharacterId bidder, AccountId bidderAccount,
        CancellationToken cancellationToken) =>
        await database.ExecuteAsync("""
            UPDATE auction_listings SET current_bid=@amount,top_bidder_character_id=@bidder,top_bidder_account_id=@account
            WHERE listing_id=@id AND state=0
            """,
            p =>
            {
                p.AddWithValue("@id", listing.Value); p.AddWithValue("@amount", amount);
                p.AddWithValue("@bidder", bidder.Value); p.AddWithValue("@account", bidderAccount.Value);
            }, cancellationToken).ConfigureAwait(false) == 1;

    public async ValueTask<bool> ReduceAsync(AuctionListingId listing, int quantityRemaining, long buyoutPrice,
        CancellationToken cancellationToken) =>
        await database.ExecuteAsync("""
            UPDATE auction_listings SET quantity_remaining=@remaining,buyout_price=@buyout
            WHERE listing_id=@id AND state=0 AND quantity_remaining>@remaining
            """,
            p =>
            {
                p.AddWithValue("@id", listing.Value); p.AddWithValue("@remaining", quantityRemaining);
                p.AddWithValue("@buyout", buyoutPrice);
            }, cancellationToken).ConfigureAwait(false) == 1;

    public async ValueTask<bool> CloseAsync(AuctionListingId listing, AuctionListingState state, DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (state == AuctionListingState.Active) throw new ArgumentOutOfRangeException(nameof(state));
        return await database.ExecuteAsync(
            "UPDATE auction_listings SET state=@state,closed_at=@at WHERE listing_id=@id AND state=0",
            p =>
            {
                p.AddWithValue("@id", listing.Value); p.AddWithValue("@state", (int)state);
                p.AddWithValue("@at", at.ToUnixTimeSeconds());
            }, cancellationToken).ConfigureAwait(false) == 1;
    }

    public async ValueTask RecordTradeAsync(AuctionListing listing, int quantity, long price, long fee, CharacterId buyer,
        DateTimeOffset at, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO auction_trades(listing_id,market,item_id,quantity,price,fee,seller_character_id,buyer_character_id,settled_at)
            VALUES(@listing,@market,@item,@quantity,@price,@fee,@seller,@buyer,@at)
            """,
            p =>
            {
                p.AddWithValue("@listing", listing.Id.Value); p.AddWithValue("@market", (int)listing.Market);
                p.AddWithValue("@item", listing.ItemId); p.AddWithValue("@quantity", quantity);
                p.AddWithValue("@price", price); p.AddWithValue("@fee", fee);
                p.AddWithValue("@seller", listing.Seller.Value); p.AddWithValue("@buyer", buyer.Value);
                p.AddWithValue("@at", at.ToUnixTimeSeconds());
            }, cancellationToken).ConfigureAwait(false);

    public ValueTask<IReadOnlyList<AuctionListingId>> DueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            "SELECT listing_id FROM auction_listings WHERE state=0 AND expires_at<=@now ORDER BY expires_at,listing_id LIMIT @limit",
            p => { p.AddWithValue("@now", now.ToUnixTimeSeconds()); p.AddWithValue("@limit", limit); },
            r => new AuctionListingId(r.GetInt64(0)), cancellationToken);

    public async ValueTask<IReadOnlyList<AuctionListing>> ActiveAsync(AuctionMarket market, IReadOnlyList<int>? itemIds,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var filter = itemIds is { Count: > 0 }
            ? " AND item_id IN (" + string.Join(',', itemIds.Distinct().Select((_, index) => "@i" + index)) + ")"
            : string.Empty;
        return await database.ReadAllAsync(
            $"SELECT {Columns} FROM auction_listings WHERE market=@market AND state=0 AND expires_at>@now{filter} ORDER BY listing_id",
            p =>
            {
                p.AddWithValue("@market", (int)market); p.AddWithValue("@now", now.ToUnixTimeSeconds());
                if (itemIds is { Count: > 0 })
                {
                    var index = 0;
                    foreach (var item in itemIds.Distinct()) p.AddWithValue("@i" + index++, item);
                }
            }, Read, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<AuctionListing>> ActiveBySellerAsync(CharacterId seller, AuctionMarket market, int limit,
        CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            $"SELECT {Columns} FROM auction_listings WHERE seller_character_id=@who AND market=@market AND state=0 ORDER BY listing_id LIMIT @limit",
            p => { p.AddWithValue("@who", seller.Value); p.AddWithValue("@market", (int)market); p.AddWithValue("@limit", limit); },
            Read, cancellationToken);

    public ValueTask<IReadOnlyList<AuctionListing>> ActiveByTopBidderAsync(CharacterId bidder, AuctionMarket market, int limit,
        CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            $"SELECT {Columns} FROM auction_listings WHERE top_bidder_character_id=@who AND market=@market AND state=0 ORDER BY listing_id LIMIT @limit",
            p => { p.AddWithValue("@who", bidder.Value); p.AddWithValue("@market", (int)market); p.AddWithValue("@limit", limit); },
            Read, cancellationToken);

    public async ValueTask<(long Price, long Quantity)> TradedSinceAsync(AuctionMarket market, int itemId, DateTimeOffset since,
        CancellationToken cancellationToken) =>
        (await database.ReadAllAsync("""
            SELECT COALESCE(SUM(price),0),COALESCE(SUM(quantity),0) FROM auction_trades
            WHERE market=@market AND item_id=@item AND settled_at>=@since
            """,
            p =>
            {
                p.AddWithValue("@market", (int)market); p.AddWithValue("@item", itemId);
                p.AddWithValue("@since", since.ToUnixTimeSeconds());
            },
            r => (r.GetInt64(0), r.GetInt64(1)), cancellationToken).ConfigureAwait(false))[0];

    public async ValueTask<bool> HasActiveObligationsAsync(CharacterId character, CancellationToken cancellationToken) =>
        (await database.ReadAllAsync("""
            SELECT EXISTS(SELECT 1 FROM auction_listings WHERE state=0
                AND (seller_character_id=@who OR top_bidder_character_id=@who))
            """, p => p.AddWithValue("@who", character.Value), r => r.GetInt32(0), cancellationToken).ConfigureAwait(false))[0] != 0;

    private static AuctionListing Read(SqliteDataReader r) => new(
        new AuctionListingId(r.GetInt64(0)),
        (AuctionMarket)r.GetInt32(1),
        (AuctionListingState)r.GetInt32(2),
        new CharacterId(r.GetInt32(3)),
        new AccountId(r.GetInt32(4)),
        (byte[])r.GetValue(5),
        r.GetInt32(6),
        r.GetInt32(7),
        r.GetInt32(8),
        r.GetInt32(9),
        new AuctionPricing(Nullable(r, 10), Nullable(r, 11), Nullable(r, 12)),
        Nullable(r, 13),
        r.IsDBNull(14) ? null : new CharacterId(r.GetInt32(14)),
        r.IsDBNull(15) ? null : new AccountId(r.GetInt32(15)),
        r.GetInt32(16),
        r.GetInt32(17),
        DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(18)),
        DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(19)),
        r.IsDBNull(20) ? null : InventoryItemCore.Decode((byte[])r.GetValue(20)));

    private static long? Nullable(SqliteDataReader r, int ordinal) => r.IsDBNull(ordinal) ? null : r.GetInt64(ordinal);
}
