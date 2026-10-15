using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class DevilContractPurchaseTests
{
    [SkippableTheory]
    [InlineData(false, 100625, 60)]
    [InlineData(false, 103156, 30)]
    [InlineData(true, 100625, 60)]
    [InlineData(true, 103156, 30)]
    public async Task Bundle_purchase_grants_all_eight_services_and_renews_existing_terms(bool archive, int product, int days)
    {
        using var shop = Shop(archive);
        var now = shop.Host.Clock.GetUtcNow().ToUnixTimeSeconds();
        shop.Host.Sql($"INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,580,{now + 86400}),(1,581,1)");
        var input = CeraShopTestContent.Entry(product) with { Attribute = 255, Variant = 0 };
        await shop.Handle(CeraShopTestContent.Input(input));
        Assert.Equal(100000 - 3880, shop.Cera);
        Assert.Equal(8, shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums"));
        for (var type = 580; type <= 587; type++)
            Assert.Equal(now + days * 86400L + (type == 580 ? 86400 : 0),
                shop.Host.Scalar($"SELECT end_time FROM account_premiums WHERE premium_type={type}"));
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM items"));
        Assert.Contains(shop.Host.Sender.Messages, message => shop.Encode(message)[0] == 1 && BitConverter.ToUInt16(shop.Encode(message), 1) == 0x40);
        var snapshot = shop.Encode(Assert.Single(shop.Host.Sender.Messages,
            message => BitConverter.ToUInt16(shop.Encode(message), 1) == 0x036F));
        for (var slot = 0; slot < 8; slot++)
            Assert.Equal((uint)shop.Host.Scalar($"SELECT end_time FROM account_premiums WHERE premium_type={580 + slot}"),
                BitConverter.ToUInt32(snapshot, 24 + 9 * slot));
        var renewed = await shop.Buy(input);
        Assert.NotNull(renewed.Receipt);
        Assert.Equal(100000 - 3880 * 2, shop.Cera);
        for (var type = 580; type <= 587; type++)
            Assert.Equal(now + days * 2 * 86400L + (type == 580 ? 86400 : 0),
                shop.Host.Scalar($"SELECT end_time FROM account_premiums WHERE premium_type={type}"));
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bundle_cannot_partially_charge_or_grant_when_a_later_service_write_fails(bool archive)
    {
        using var shop = Shop(archive);
        shop.Host.Sql("CREATE TRIGGER fail_bundle BEFORE INSERT ON account_premiums WHEN NEW.premium_type=586 BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => shop.Buy(CeraShopTestContent.Entry(100625)));
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums"));
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Insufficient_funds_and_unexpected_options_do_not_grant_any_bundle_service(bool archive)
    {
        using var shop = Shop(archive);
        shop.Host.Sql("UPDATE accounts SET cera=3879,token_cera=0,happy_token_cera=0");
        Assert.Equal(CeraShopRejection.InsufficientFunds, (await shop.Buy(CeraShopTestContent.Entry(100625))).Rejection);
        shop.Host.Sql("UPDATE accounts SET cera=100000");
        Assert.Equal(CeraShopRejection.InvalidChoice, (await shop.Buy(CeraShopTestContent.Entry(100625) with { Attribute = 1 })).Rejection);
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums"));
    }

    private static CeraShopFixture Shop(bool archive)
    {
        Skip.If(archive && new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        var shop = new CeraShopFixture(useArchive: archive, configure: services =>
        {
            if (!archive) services.AddSingleton<IPremiumServiceDefinitionSource>(new ServiceRules());
        });
        if (!archive)
            foreach (var (product, days) in new[] { (100625, 60), (103156, 30) })
                shop.Content.Products[product] = new(product, 2682006, 1, "[charac premium package]",
                    new(0, 0, 3880, 0, []), true, false, false, [], "fixture", ServiceDays: days)
                    { ServiceSlots = [0, 1, 2, 3, 4, 5, 6, 7] };
        return shop;
    }

    private sealed class ServiceRules : IPremiumServiceDefinitionSource
    {
        public DoublePotRules ReadDoublePotRules() => new(8, new HashSet<int>());
        public IReadOnlyList<int> ReadQuestHelperMinimumDropRates() => [100, 70, 30];
        public PremiumContractLevels ReadContractLevels() => new(5, 10);
        public double ReadGrowthExperienceBonusRate() => .2;
        public double ReadBlackDiamondExperienceBonusRate() => .05;
        public IReadOnlyList<int> ReadGrowthBossEquipmentBonusPercents() => [20, 23, 27, 30];
        public int ReadFreeWeaknessDailyLimit() => 10;
        public int ReadDungeonBuffDailyLimit() => 10;
        public int ReadAutoRepairDailyLimit() => 6;
        public int ReadFreeGoldCardDailyLimit() => 10;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Invalid_bundle_declarations_do_not_charge_or_grant(int invalid)
    {
        using var shop = Shop(false);
        var product = shop.Content.Products[100625];
        shop.Content.Products[100625] = invalid switch
        {
            0 => product with { ServiceSlots = [] },
            1 => product with { ServiceSlots = [0, 0] },
            2 => product with { ServiceSlots = [0, 8] },
            _ => product with { ServiceDays = 0 },
        };
        Assert.Equal(CeraShopRejection.UnsupportedProduct, (await shop.Buy(CeraShopTestContent.Entry(100625))).Rejection);
        Assert.Equal(100000, shop.Cera);
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums"));
    }
}
