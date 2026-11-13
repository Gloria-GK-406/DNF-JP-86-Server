using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Auction;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dfo.Server.Persistence.Tests.Auction;

/// <summary>Listings and their custody as written to the real schema.</summary>
public sealed class AuctionCustodyAndStoreTests : IDisposable
{
    private static readonly ContainerId Bag = new("character:71:Main");
    private static readonly ContainerId BuyerBag = new("character:72:Main");
    private static readonly DateTimeOffset At = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private readonly string directory = Path.Combine(Path.GetTempPath(), $"dfo-auction-{Guid.NewGuid():N}");
    private readonly string path;
    private readonly DomainTransactionScope scope = TestRequired.TransactionScope();

    public AuctionCustodyAndStoreTests()
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "auction.db");
        _ = new SqliteDatabase(path, scope);
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO accounts(account_id,m_id) VALUES(1,'one'),(2,'two');
            INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,delete_flag) VALUES
                (71,1,'seller',0,0,1,0,0),(72,2,'buyer',0,0,1,0,0);
            """;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }

    private static ItemSnapshot Sword() => new() { ItemId = 123, LegacyKind = 1, Type = ItemTraits.Equipment, Count = 1,
        InstanceValue = 4321, Durability = 31, Attribute = 7 };

    private static ItemSnapshot Stones(int count) => new() { ItemId = 3037, LegacyKind = 2, Type = ItemTraitClassification.For(2, 3037),
        Count = count, IsPile = true };

    private SqliteAuctionDomainStore Listings() => new(new SqliteDatabase(path, scope));

    private SqliteItemDomainStore Items() => new(path, contentsEvents: TestRequired.NoContentsEvents);

    private static AuctionListing Listing(int item, int quantity, AuctionPricing pricing) => new(default, AuctionMarket.Items,
        AuctionListingState.Active, new CharacterId(71), new AccountId(1), "seller"u8.ToArray(), item, quantity, quantity, 0,
        pricing, null, null, null, 10_000, 500, At, At.AddHours(24), null);

    private Task InTransaction(Func<IItemDomainStoreSession, SqliteAuctionDomainStore, Task> body) => TestGameplay.RunAsync(async () =>
    {
        await using var transaction = scope.Begin();
        await body(Items().SessionIn(scope.Current!), Listings());
        await transaction.CommitAsync(default);
    });

    [Fact]
    public async Task Equipment_keeps_its_identity_and_bytes_through_custody()
    {
        ItemRecord? listed = null;
        AuctionListingId id = default;
        await InTransaction(async (items, store) =>
        {
            await items.PutAsync(Bag, 9, await items.CreateAsync(Sword(), default), default);
            listed = await items.TakeAsync((await items.GetAsync(Bag, 9, default))!, null, default);
            id = await store.InsertAsync(Listing(123, 1, new(1000, 5000)), default);
            await ((IAuctionItemCustodySession)items).AcceptAuctionLotAsync(id, listed, default);
        });
        await InTransaction(async (items, _) =>
        {
            var record = await ((IAuctionItemCustodySession)items).TakeAuctionLotAsync(id, null, default);
            Assert.Equal(listed!.ItemUid, record.ItemUid);
            Assert.Equal(listed.Snapshot.ToCore(), record.Snapshot.ToCore());
            await items.PutAsync(BuyerBag, 3, record, default);
        });
        await InTransaction(async (items, _) =>
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ((IAuctionItemCustodySession)items).TakeAuctionLotAsync(id, null, default).AsTask()));
    }

    [Fact]
    public async Task A_pile_is_taken_out_in_parts_and_custody_ends_with_the_last_part()
    {
        AuctionListingId id = default;
        await InTransaction(async (items, store) =>
        {
            await items.PutAsync(Bag, 20, await items.CreateAsync(Stones(50), default), default);
            var taken = await items.TakeAsync((await items.GetAsync(Bag, 20, default))!, 30, default);
            id = await store.InsertAsync(Listing(3037, 30, new(null, 3000, 100)), default);
            await ((IAuctionItemCustodySession)items).AcceptAuctionLotAsync(id, taken, default);
            Assert.Equal(20, (await items.GetAsync(Bag, 20, default))!.Count);
        });
        await InTransaction(async (items, _) =>
        {
            var part = await ((IAuctionItemCustodySession)items).TakeAuctionLotAsync(id, 12, default);
            Assert.Equal(12, part.Count);
            await items.PutAsync(BuyerBag, 20, part, default);
        });
        await InTransaction(async (items, _) =>
        {
            var custody = (IAuctionItemCustodySession)items;
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => custody.TakeAuctionLotAsync(id, 19, default).AsTask());
            var rest = await custody.TakeAuctionLotAsync(id, null, default);
            Assert.Equal(18, rest.Count);
            await items.PutAsync(BuyerBag, 20, rest, default);
            Assert.Equal(30, (await items.GetAsync(BuyerBag, 20, default))!.Count);
        });
    }

    [Fact]
    public async Task A_taken_lot_that_nobody_receives_cannot_commit()
    {
        AuctionListingId id = default;
        await InTransaction(async (items, store) =>
        {
            id = await store.InsertAsync(Listing(123, 1, new(null, 5000)), default);
            await ((IAuctionItemCustodySession)items).AcceptAuctionLotAsync(id, await items.CreateAsync(Sword(), default), default);
        });
        await TestGameplay.RunAsync(async () =>
        {
            await using var transaction = scope.Begin();
            var items = Items().SessionIn(scope.Current!);
            _ = await ((IAuctionItemCustodySession)items).TakeAuctionLotAsync(id, null, default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync(default).AsTask());
        });
    }

    [Fact]
    public async Task A_listing_reaches_one_final_state_and_leaves_the_due_and_active_reads()
    {
        AuctionListingId id = default;
        await InTransaction(async (_, store) =>
        {
            id = await store.InsertAsync(Listing(123, 1, new(1000, 5000)), default);
            Assert.True(await store.SetBidAsync(id, 1200, new CharacterId(72), new AccountId(2), default));
            Assert.Equal(1, await store.CountActiveAsync(new CharacterId(71), AuctionMarket.Items, default));
            Assert.True(await store.HasActiveObligationsAsync(new CharacterId(72), default));
        });
        await InTransaction(async (_, store) =>
        {
            Assert.Empty(await store.DueAsync(At.AddHours(23), 10, default));
            Assert.Equal([id], await store.DueAsync(At.AddHours(24), 10, default));
            var listing = (await store.FindAsync(id, default))!;
            Assert.Equal((1200L, new CharacterId(72)), (listing.CurrentBid!.Value, listing.TopBidder!.Value));
            Assert.True(await store.CloseAsync(id, AuctionListingState.Sold, At.AddHours(24), default));
            Assert.False(await store.CloseAsync(id, AuctionListingState.Expired, At.AddHours(24), default));
            await store.RecordTradeAsync(listing, 1, 1200, 60, new CharacterId(72), At.AddHours(24), default);
        });
        await InTransaction(async (_, store) =>
        {
            Assert.Empty(await store.DueAsync(At.AddHours(25), 10, default));
            Assert.Empty(await store.ActiveAsync(AuctionMarket.Items, null, At, default));
            Assert.False(await store.HasActiveObligationsAsync(new CharacterId(72), default));
            Assert.Equal((1200L, 1L), await store.TradedSinceAsync(AuctionMarket.Items, 123, At, default));
        });
    }
}
