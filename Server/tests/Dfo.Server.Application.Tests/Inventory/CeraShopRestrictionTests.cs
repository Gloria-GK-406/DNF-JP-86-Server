using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers.Binary;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class CeraShopRestrictionTests
{
    private static CeraShopFixture RestrictedShop(int days = 30)
    {
        var shop = new CeraShopFixture();
        shop.Host.Sql("INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0),(2,0)");
        shop.Content.Products[110001] = shop.Content.Products[110001] with
        { PurchaseRestriction = new(32, days, 1), Count = 1 };
        return shop;
    }

    [Fact]
    public async Task Purchase_limit_is_account_wide_survives_entry_projection_and_expires_at_boundary()
    {
        using var shop = RestrictedShop();
        var entry = CeraShopTestContent.Entry(110001);
        var first = await shop.Buy(entry);
        Assert.NotNull(first.Receipt);
        var now = shop.Host.Clock.GetUtcNow();
        var expectedEnd = checked((int)now.AddDays(30).ToUnixTimeSeconds());
        Assert.Equal(expectedEnd, shop.Host.Scalar("SELECT end_time FROM account_purchase_restrictions WHERE account_id=1 AND restriction_id=32"));
        Assert.Equal(new AccountCompatibilityEntryState(32, expectedEnd, 0), Assert.Single(first.Receipt.PurchaseRestrictions!));
        var service = shop.Host.Services.GetRequiredService<IAccountEntryStateDomainService>();
        var entered = await service.LoadAsync(new(1), default);
        Assert.Equal(first.Receipt.PurchaseRestrictions, entered.Relations.Unknown730Entries);
        shop.Host.Select(2);
        Assert.Equal(CeraShopRejection.PurchaseRestricted, (await shop.Buy(entry)).Rejection);
        Assert.Equal(99980, shop.Cera);
        shop.Host.Clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1));
        Assert.Equal(CeraShopRejection.PurchaseRestricted, (await shop.Buy(entry)).Rejection);
        shop.Host.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, Assert.Single((await service.LoadAsync(new(1), default)).Relations.Unknown730Entries).Flag);
        Assert.NotNull((await shop.Buy(entry)).Receipt);
        Assert.Equal(99960, shop.Cera);
        Assert.Equal(1, shop.Host.Scalar("SELECT COUNT(*) FROM account_purchase_restrictions"));
    }

    [Fact]
    public async Task Duplicate_cart_and_insufficient_funds_do_not_claim_restriction()
    {
        using var shop = RestrictedShop();
        var entry = CeraShopTestContent.Entry(110001);
        Assert.Equal(CeraShopRejection.PurchaseRestricted, (await shop.Buy(entry, entry)).Rejection);
        Assert.Equal(100000, shop.Cera);
        shop.Host.Sql("UPDATE accounts SET cera=0");
        Assert.Equal(CeraShopRejection.InsufficientFunds, (await shop.Buy(entry)).Rejection);
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM account_purchase_restrictions"));
        Assert.Equal(0, shop.Host.Count(6000));
        shop.Host.Sql("UPDATE accounts SET cera=100000");
        Assert.NotNull((await shop.Buy(entry)).Receipt);
    }

    [Fact]
    public async Task Late_item_write_failure_rolls_back_limit_payment_and_rewards()
    {
        using var shop = RestrictedShop();
        shop.Host.Sql("CREATE TRIGGER fail_reward BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'forced late reward failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => shop.Buy(CeraShopTestContent.Entry(110001)));
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM account_purchase_restrictions"));
        Assert.Equal(0, shop.Host.Count(6000));
        shop.Host.Sql("DROP TRIGGER fail_reward");
        Assert.NotNull((await shop.Buy(CeraShopTestContent.Entry(110001))).Receipt);
    }

    [Fact]
    public async Task Full_bag_rejection_does_not_charge_or_claim_limit()
    {
        using var shop = RestrictedShop();
        foreach (var slot in new[] { 65, 66, 3, 4, 5, 6, 7, 8 }) await shop.Host.Seed(slot, 3, 10);
        Assert.Equal(CeraShopRejection.NoRoom, (await shop.Buy(CeraShopTestContent.Entry(110001))).Rejection);
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM account_purchase_restrictions"));
    }

    [Fact]
    public async Task Restriction_notification_contains_all_entries_because_client_replaces_the_list()
    {
        using var shop = RestrictedShop();
        shop.Host.Sql("INSERT INTO account_purchase_restrictions VALUES(1,9,2147439600)");
        await shop.Handle(CeraShopTestContent.Input(CeraShopTestContent.Entry(110001)));
        var message = Assert.Single(shop.Host.Sender.Messages, m => m.GetType().Name == "PurchaseRestrictionCompatibilityMessage");
        var frame = shop.Encode(message);
        Assert.Equal((ushort)0x0344, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(15, 4)));
        Assert.Equal(9, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(19, 4)));
        Assert.Equal(32, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(31, 4)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(27, 4)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(39, 4)));
    }

    [Fact]
    public async Task Zero_period_limit_is_permanent_and_other_accounts_remain_independent()
    {
        using var shop = RestrictedShop(0);
        Assert.NotNull((await shop.Buy(CeraShopTestContent.Entry(110001))).Receipt);
        shop.Host.Clock.Advance(TimeSpan.FromDays(365));
        Assert.Equal(CeraShopRejection.PurchaseRestricted, (await shop.Buy(CeraShopTestContent.Entry(110001))).Rejection);
        var other = await shop.Host.Services.GetRequiredService<ICeraShopPurchaseRestrictionDomainService>().ReadAsync(new(2), shop.Host.Clock.GetUtcNow(), default);
        Assert.Empty(other.Entries);
    }

    [SkippableFact]
    public async Task Light_heart_package_purchase_delivers_fixed_and_selected_rewards_once()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var entry = CeraShopTestContent.Entry(101236) with
        {
            Selections = [new(2682780, 0, 0)],
        };
        var result = await shop.Buy(entry);
        Assert.True(result.Receipt is not null, $"{result.Rejection}: {result.Detail}");
        Assert.Equal(98040, shop.Cera);
        foreach (var (id, count) in new (int, int)[]
        {
            (1,10),(8,50),(2682773,100),(2682767,20),(2660672,50),
            (2682770,20),(2682771,30),(2682776,30),(2682772,2),(2682777,50),(2682774,30),
        })
            Assert.Equal(count, shop.Host.Count(id));
        Assert.Equal(0, shop.Host.Count(2682780));
        Assert.Equal(0, shop.Host.Count(2682775));
        var repeated = await shop.Buy(entry);
        Assert.Null(repeated.Receipt);
        Assert.Equal(98040, shop.Cera);
        Assert.Equal(30, shop.Host.Count(2682774));
    }

    [SkippableFact]
    public async Task Light_heart_second_selection_delivers_only_that_choice()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var result = await shop.Buy(CeraShopTestContent.Entry(101236) with { Selections = [new(2682780, 0, 1)] });
        Assert.True(result.Receipt is not null, $"{result.Rejection}: {result.Detail}");
        Assert.Equal(30, shop.Host.Count(2682775));
        Assert.Equal(0, shop.Host.Count(2682774));
    }
}
