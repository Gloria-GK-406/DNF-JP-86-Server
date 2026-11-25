using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Auction;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Mail;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Mail;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dfo.Server.Persistence.Tests.Auction;

public sealed partial class AuctionCustodyAndStoreTests
{
    [Theory]
    [InlineData("cancel", AuctionListingState.Cancelled, 12, 18, 1140)]
    [InlineData("expire", AuctionListingState.Expired, 12, 18, 11140)]
    [InlineData("buyout", AuctionListingState.Sold, 30, 0, 12850)]
    [InlineData("last", AuctionListingState.Sold, 30, 0, 12850)]
    public async Task Partial_purchase_then_closure_settles_items_money_mail_and_history_together(
        string ending, AuctionListingState expectedState, int bought, int returned, int sellerGold)
    {
        var house = SettlementHouse();
        var id = await SeedSettlementAsync();
        await InTransaction(async (_, _) => await house.BuyPiecesAsync(new(72), id, 12, 1200, default));
        Assert.Equal(18, Scalar("SELECT item_count FROM auction_custody"));
        Assert.Equal(98800, Scalar("SELECT carried_gold FROM character_stats WHERE character_id=72"));
        await InTransaction(async (_, _) =>
        {
            switch (ending)
            {
                case "cancel": await house.CancelAsync(new(71), AuctionMarket.Items, id, default); break;
                case "expire": Assert.True(await house.SettleExpiredAsync(id, At.AddHours(25), default)); break;
                case "buyout": await house.BidAsync(new(72), AuctionMarket.Items, id, 1800, default); break;
                case "last": await house.BuyPiecesAsync(new(72), id, 18, 1800, default); break;
            }
        });
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM auction_custody"));
        Assert.Equal((int)expectedState, Scalar("SELECT state FROM auction_listings"));
        Assert.Equal(18, Scalar("SELECT quantity_at_close FROM auction_listings"));
        Assert.Equal(100000 - bought * 100, Scalar("SELECT carried_gold FROM character_stats WHERE character_id=72"));
        Assert.Equal(bought, Mailed(72, "item_count", "stackable"));
        Assert.Equal(returned, Mailed(71, "item_count", "stackable"));
        Assert.Equal(sellerGold, Mailed(71, "gold_amount", "gold"));
        Assert.Equal(bought, Scalar("SELECT SUM(quantity) FROM auction_trades"));
        Assert.Equal(bought * 100, Scalar("SELECT SUM(price) FROM auction_trades"));
        Assert.Equal(bought * 5, Scalar("SELECT SUM(fee) FROM auction_trades"));
    }

    [Theory]
    [InlineData(false, "auction_trades")]
    [InlineData(true, "auction_trades")]
    [InlineData(false, "mailbox_attachments")]
    [InlineData(true, "mailbox_attachments")]
    public async Task Failure_after_taking_assets_rolls_back_the_entire_purchase(bool last, string failureTable)
    {
        var house = SettlementHouse();
        var id = await SeedSettlementAsync();
        // Test-owned constant identifiers; the trigger fails after money and/or item custody changed.
        Execute($"CREATE TRIGGER reject_settlement BEFORE INSERT ON {failureTable} BEGIN SELECT RAISE(ABORT,'forced settlement failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => InTransaction(async (_, _) =>
            await house.BuyPiecesAsync(new(72), id, last ? 30 : 12, last ? 3000 : 1200, default)));
        Assert.Equal(30, Scalar("SELECT item_count FROM auction_custody"));
        Assert.Equal(0, Scalar("SELECT state FROM auction_listings"));
        Assert.Equal(1, Scalar("SELECT quantity_at_close IS NULL FROM auction_listings"));
        Assert.Equal(100000, Scalar("SELECT carried_gold FROM character_stats WHERE character_id=72"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM auction_trades"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM mailbox_messages"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM mailbox_attachments"));
        Execute("DROP TRIGGER reject_settlement");
        await InTransaction(async (_, _) => await house.BuyPiecesAsync(new(72), id, 30, 3000, default));
        Assert.Equal(30, Mailed(72, "item_count", "stackable"));
    }

    private async Task<AuctionListingId> SeedSettlementAsync()
    {
        Execute("INSERT INTO character_stats(character_id,carried_gold) VALUES(71,90000),(72,100000)");
        AuctionListingId id = default;
        await InTransaction(async (items, store) =>
        {
            id = await store.InsertAsync(Listing(3037, 30, new(null, 3000, 100)), default);
            await ((IAuctionItemCustodySession)items).AcceptAuctionLotAsync(id, await items.CreateAsync(Stones(30), default), default);
        });
        return id;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gold_consignment_settles_without_item_custody_and_keeps_wallet_rollback(bool failFirst)
    {
        var house = SettlementHouse();
        Execute("UPDATE accounts SET cera=1000 WHERE account_id=2");
        AuctionListingId id = default;
        await InTransaction(async (_, store) => id = await store.InsertAsync(
            Listing(2681725, 1, new(null, 100)) with { Market = AuctionMarket.GoldConsignment, GoldAmount = 1_000_000, Deposit = 0 }, default));
        if (failFirst)
        {
            Execute("CREATE TRIGGER reject_gold_trade BEFORE INSERT ON auction_trades BEGIN SELECT RAISE(ABORT,'forced gold failure'); END;");
            await Assert.ThrowsAsync<SqliteException>(() => InTransaction(async (_, _) =>
                await house.BidAsync(new(72), AuctionMarket.GoldConsignment, id, 100, default)));
            Assert.Equal(1000, Scalar("SELECT cera FROM accounts WHERE account_id=2"));
            Assert.Equal(0, Scalar("SELECT COUNT(*) FROM mailbox_messages"));
            Assert.Equal(0, Scalar("SELECT state FROM auction_listings"));
            Execute("DROP TRIGGER reject_gold_trade");
        }
        await InTransaction(async (_, _) => await house.BidAsync(new(72), AuctionMarket.GoldConsignment, id, 100, default));
        Assert.Equal(900, Scalar("SELECT cera FROM accounts WHERE account_id=2"));
        Assert.Equal(1_000_000, Mailed(72, "gold_amount", "gold"));
        Assert.Equal(95, Mailed(71, "gold_amount", "token_cera"));
        Assert.Equal(1, Scalar("SELECT quantity_at_close FROM auction_listings"));
        Assert.Equal(1, Scalar("SELECT quantity FROM auction_trades"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM auction_custody"));
    }

    [Fact]
    public async Task An_expired_equipment_bid_delivers_the_original_identity_without_charging_twice()
    {
        var house = SettlementHouse();
        Execute("INSERT INTO character_stats(character_id,carried_gold) VALUES(71,90000),(72,100000)");
        AuctionListingId id = default;
        long uid = 0;
        await InTransaction(async (items, store) =>
        {
            id = await store.InsertAsync(Listing(123, 1, new(1000, 5000)), default);
            var record = await items.CreateAsync(Sword(), default);
            uid = record.ItemUid!.Value.Value;
            await ((IAuctionItemCustodySession)items).AcceptAuctionLotAsync(id, record, default);
        });
        await InTransaction(async (_, _) => await house.BidAsync(new(72), AuctionMarket.Items, id, 1500, default));
        await InTransaction(async (_, _) => Assert.True(await house.SettleExpiredAsync(id, At.AddHours(25), default)));
        Assert.Equal(98500, Scalar("SELECT carried_gold FROM character_stats WHERE character_id=72"));
        Assert.Equal(uid, Scalar("SELECT source_item_uid FROM mailbox_attachments WHERE asset_kind='equipment'"));
        Assert.Equal(11425, Mailed(71, "gold_amount", "gold"));
        Assert.Equal(1, Scalar("SELECT quantity FROM auction_trades"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM auction_custody"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_lost_conditional_take_faults_the_session_and_rolls_back_custody(bool auction)
    {
        AuctionListingId id = default;
        await InTransaction(async (items, store) =>
        {
            var record = await items.CreateAsync(Stones(30), default);
            if (auction)
            {
                id = await store.InsertAsync(Listing(3037, 30, new(null, 3000, 100)), default);
                await ((IAuctionItemCustodySession)items).AcceptAuctionLotAsync(id, record, default);
            }
            else await ((IPrivateStoreItemCustodySession)items).AcceptPrivateStoreLineAsync(new(71), 0, 100, record, default);
        });
        var table = auction ? "auction_custody" : "private_store_custody";
        Execute($"CREATE TRIGGER lose_take BEFORE UPDATE ON {table} BEGIN SELECT RAISE(IGNORE); END;");
        await TestGameplay.RunAsync(async () =>
        {
            await using var transaction = scope.Begin();
            var items = Items().SessionIn(scope.Current!);
            await Assert.ThrowsAsync<InvalidOperationException>(() => auction
                ? ((IAuctionItemCustodySession)items).TakeAuctionLotAsync(id, 12, default).AsTask()
                : ((IPrivateStoreItemCustodySession)items).TakePrivateStoreLineAsync(new(71), 0, 12, default).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync(default).AsTask());
        });
        Assert.Equal(30, Scalar($"SELECT item_count FROM {table}"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM item_slots"));
    }

    private AuctionHouseDomainService SettlementHouse()
    {
        var database = new SqliteDatabase(path, scope);
        var owners = new SqliteCharacterTable(database);
        var events = new DiscardingEventService();
        var clock = new SettlementClock();
        var characters = new CharacterDomainService(new SqliteCharacterCarriedGoldTable(database),
            TestRequired.Absent<ICarriedGoldRuleService>(), owners, TestRequired.Absent<ICharacterSkillPointDomainService>(),
            TestRequired.Absent<ICharacterStatRuleService>(), TestRequired.Absent<ICharacterSkillStateDomainService>(),
            TestRequired.Absent<IExperienceRuleService>(), TestRequired.Absent<IExperienceCapsuleDomainService>(),
            TestRequired.InitialHonor, scope, events);
        var wallets = new AccountWalletDomainService(new SqliteAccountTable(database), new AccountWalletRuleService(), scope);
        var sendRules = TestRequired.Absent<IMailItemRuleService>();
        var inventories = TestRequired.Absent<ICharacterBusinessInventoryDomainService>();
        var transfers = new MailItemTransfers(TestRequired.Absent<IItemPlacementRuleService>(), inventories,
            TestRequired.Absent<IItemArrivalRuleService>(), TestRequired.Absent<IItemLifecycleDomainService>(), sendRules, owners);
        var mailbox = new MailboxDomainService(new SqliteMailboxRepository(database, Items(), scope), characters,
            owners, transfers, scope, clock, events, wallets);
        var rules = new AuctionRuleService(TestRequired.Absent<IAuctionItemDefinitionSource>(), sendRules,
            new ConfigurationBuilder().Build());
        return new AuctionHouseDomainService(Listings(), rules,
            new AuctionLotHandling(rules, inventories, characters, Items(), scope, mailbox),
            new AuctionPurse(characters, wallets, mailbox), characters, owners,
            TestRequired.Absent<IPremiumServiceDomainService>(), scope, clock);
    }

    private sealed class SettlementClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => At;
    }

    private long Mailed(int recipient, string value, string kind) => Scalar($"""
        SELECT COALESCE(SUM(a.{value}),0) FROM mailbox_attachments a JOIN mailbox_messages m ON m.message_id=a.message_id
        WHERE m.receiver_character_id={recipient} AND a.asset_kind='{kind}'
        """);

    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
