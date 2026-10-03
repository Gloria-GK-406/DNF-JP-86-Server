using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.RepairItem;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentRepairTests
{
    [Theory]
    [InlineData(0xA0, 7, 113)]
    [InlineData(0xA1, 0, 226)]
    [InlineData(0xA1, 7, 226)]
    public async Task Repair_quote_uses_attribute_reinforcement_and_preserves_independent_enchant_count(
        byte attribute, byte enchantUpgrade, int expectedCost)
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(0) with { Attribute = attribute, EnchantUpgradeCount = enchantUpgrade },
            InventoryContainerType.Equipment);

        await Repair(shop, InventoryContainerType.Equipment, 12);

        Assert.Equal(100000 - expectedCost, shop.Gold);
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Equipment, 12));
        var stored = shop.StoredCore(12, InventoryContainerType.Equipment);
        Assert.Equal(attribute, stored[9]);
        Assert.Equal(enchantUpgrade, stored[17]);
        Assert.Equal((byte)0, Assert.IsType<RepairItemResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
    }

    [Fact]
    public async Task Equipped_piece_costs_113_and_ack_observes_committed_gold_and_durability()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        shop.Sender.BeforeSend = () =>
        {
            Assert.Equal(99887, shop.Gold);
            Assert.Equal((ushort)45, shop.Durability(InventoryContainerType.Equipment, 12).AsTask().GetAwaiter().GetResult());
        };
        await Repair(shop, InventoryContainerType.Equipment, 12);
        var ack = Assert.IsType<RepairItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Equal((99887, InventoryContainerType.Equipment, 12),
            (ack.Gold, ack.Slot!.Value.Space, ack.Slot.Value.Index));
    }

    [Fact]
    public async Task All_repair_prices_equipment_and_quick_slots_then_returns_one_ack()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        await shop.SeedCore(3, Piece(0));
        await Repair(shop, InventoryContainerType.Main, -1);
        Assert.Equal(99774, shop.Gold);
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Equipment, 12));
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 3));
        Assert.Null(Assert.IsType<RepairItemResponse>(Assert.Single(shop.Sender.Messages)).Slot);
    }

    [Fact]
    public async Task All_repair_treats_a_nondurable_equipment_as_a_zero_cost_unchanged_piece()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        await shop.SeedCore(20, StackableItemCore.Create(1, 100310669, 4321), InventoryContainerType.Equipment);
        await Repair(shop, InventoryContainerType.Main, -1);
        Assert.Equal(99887, shop.Gold);
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Equipment, 12));
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Equipment, 20));
        Assert.Equal(99887, Assert.IsType<RepairItemResponse>(Assert.Single(shop.Sender.Messages)).Gold);
    }

    [Fact]
    public async Task Single_nondurable_repair_consumes_zero_and_does_not_write_the_item()
    {
        using var shop = Fixture();
        await shop.SeedCore(20, StackableItemCore.Create(1, 100310669, 4321), InventoryContainerType.Equipment);
        shop.Sql("CREATE TRIGGER refuse_item_write BEFORE UPDATE OF item_core ON items BEGIN SELECT RAISE(ABORT,'unexpected item write'); END;");
        await Repair(shop, InventoryContainerType.Equipment, 20);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Equipment, 20));
        var ack = Assert.IsType<RepairItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Equal((100000, 20), (ack.Gold, ack.Slot!.Value.Index));
    }

    [Fact]
    public async Task Insufficient_gold_leaves_all_durability_untouched()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        await shop.SeedCore(3, Piece(0));
        shop.Sql("UPDATE character_stats SET carried_gold=200 WHERE character_id=1");
        await Repair(shop, InventoryContainerType.Main, -1);
        Assert.Equal(200, shop.Gold);
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Equipment, 12));
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Main, 3));
        Assert.Equal(10, Assert.IsType<RepairItemResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
    }

    [Fact]
    public async Task Failed_item_commit_rolls_back_gold_and_emits_no_ack()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        shop.Sql("CREATE TRIGGER refuse_repair BEFORE UPDATE OF item_core ON items BEGIN SELECT RAISE(ABORT,'test repair failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Repair(shop, InventoryContainerType.Equipment, 12).AsTask());
        Assert.Equal(100000, shop.Gold);
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Equipment, 12));
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task Unsupported_variant_does_not_charge_or_repair()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        await shop.Services.GetRequiredService<RepairItemHandler>().HandleAsync(new(shop.Connection),
            new RepairItemRequest(
                GameMessageMetadata.Empty,
                new GameSlot(InventoryContainerType.Equipment, 12), -1, 0, 0, 1),
            default);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Equipment, 12));
        Assert.NotEqual(0, Assert.IsType<RepairItemResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
    }

    [Fact]
    public async Task Structurally_mapped_pet_equipment_is_not_a_repair_target()
    {
        using var shop = Fixture();
        await shop.SeedCore(140, Piece(0), InventoryContainerType.Pet);

        await Repair(shop, InventoryContainerType.Pet, 140);

        Assert.Equal(100000, shop.Gold);
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Pet, 140));
        Assert.Equal(10,
            Assert.IsType<RepairItemResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
    }

    [Fact]
    public async Task Replaced_character_session_cannot_repair_its_old_equipment()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        var old = shop.Session;
        shop.Select(2);
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(
            new RepairItemCommand(
                shop.OnlineUser, old,
                new BusinessSlot(BusinessInventorySpace.WornEquipment, 1)));
        Assert.Equal(EquipmentRepairRejection.NoSession, result.Rejection);
        Assert.Equal(100000, shop.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=1"));
        Assert.Empty(shop.Sender.Messages);
    }

    private static ShopFixture Fixture() => new(services =>
        {
        services.AddSingleton<IEquipmentRepairDefinitionSource>(new Prices());
        services.AddSingleton<IPremiumServiceDefinitionSource>(new PremiumRules());
    });

    private static InventoryItemCoreState Piece(ushort durability) =>
        StackableItemCore.Create(1, 3000, 1234) with { Durability = durability };

    private static ValueTask Repair(ShopFixture shop, InventoryContainerType container, short slot) =>
        shop.Services.GetRequiredService<RepairItemHandler>().HandleAsync(new(shop.Connection),
            new RepairItemRequest(
                GameMessageMetadata.Empty,
                slot == -1 ? null : new GameSlot(container, slot), -1, 0, 0, 0,
                container),
            default);

    private sealed class PremiumRules : IPremiumServiceDefinitionSource
    {
        public IReadOnlyList<int> ReadQuestHelperMinimumDropRates() => new[] { 100, 70, 30 };
        public DoublePotRules ReadDoublePotRules() => new(8, new HashSet<int>());
        public IReadOnlyList<int> ReadGrowthBossEquipmentBonusPercents() => new[] {20, 23, 27, 30};
        public double ReadBlackDiamondExperienceBonusRate() => .05;
        public double ReadGrowthExperienceBonusRate() => 0.2;
        public PremiumContractLevels ReadContractLevels() => new(5, 10);
        public int ReadFreeWeaknessDailyLimit() => 10;
        public int ReadFreeGoldCardDailyLimit() => 10;
        public int ReadDungeonBuffDailyLimit() => 10;
        public int ReadAutoRepairDailyLimit() => 6;
    }

    private static ValueTask AutoRepair(ShopFixture shop) =>
        shop.Services.GetRequiredService<RepairItemHandler>().HandleAsync(new(shop.Connection),
            new RepairItemRequest(GameMessageMetadata.Empty, null, -1, 1, 0, 0), default);

    [Fact]
    public async Task Automatic_repair_is_free_repairs_worn_and_quick_slots_once_and_preserves_bag()
    {
        using var shop = Fixture();
        shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,586,4000000000)");
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        await shop.SeedCore(14, Piece(20), InventoryContainerType.Equipment);
        await shop.SeedCore(3, Piece(0));
        await shop.SeedCore(9, Piece(7));
        await AutoRepair(shop);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Equipment, 12));
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Equipment, 14));
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 3));
        Assert.Equal((ushort)7, await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(1, shop.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='premium_auto_repair_used'"));
        Assert.IsType<RepairItemResponse>(shop.Sender.Messages[0]);
        Assert.Equal("PremiumServiceStateMessage", shop.Sender.Messages[1].GetType().Name);
        await AutoRepair(shop);
        Assert.Equal(4, Assert.IsType<RepairItemResponse>(shop.Sender.Messages.Last()).ErrorCode);
        Assert.Equal(1, shop.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='premium_auto_repair_used'"));
    }

    [Fact]
    public async Task Automatic_repair_rejects_without_broken_equipment_or_valid_subscription()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        await AutoRepair(shop);
        Assert.Equal(4, Assert.IsType<RepairItemResponse>(shop.Sender.Messages.Last()).ErrorCode);
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Equipment, 12));
        shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,586,1)");
        await AutoRepair(shop);
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
        shop.Sql("UPDATE account_premiums SET end_time=4000000000");
        await shop.SeedCore(12, Piece(1), InventoryContainerType.Equipment);
        await AutoRepair(shop);
        Assert.Equal((ushort)1, await shop.Durability(InventoryContainerType.Equipment, 12));
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Sixth_auto_repair_succeeds_seventh_is_rejected_and_next_period_recovers_quota()
    {
        using var shop = Fixture();
        shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,586,4000000000)");
        for (var i = 1; i <= 6; i++)
        {
            await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
            await AutoRepair(shop);
            Assert.Equal(i, shop.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='premium_auto_repair_used'"));
        }
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        await AutoRepair(shop);
        Assert.Equal(0x73, Assert.IsType<RepairItemResponse>(shop.Sender.Messages.Last()).ErrorCode);
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Equipment, 12));
        shop.Clock.Advance(TimeSpan.FromDays(1));
        await AutoRepair(shop);
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Equipment, 12));
        Assert.Equal(1, shop.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='premium_auto_repair_used'"));
    }

    [Fact]
    public async Task Auto_repair_write_failure_rolls_back_usage_and_durability()
    {
        using var shop = Fixture();
        shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,586,4000000000)");
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        shop.Sql("CREATE TRIGGER refuse_repair BEFORE UPDATE OF item_core ON items BEGIN SELECT RAISE(ABORT,'auto repair failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => AutoRepair(shop).AsTask());
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Equipment, 12));
        Assert.Equal(100000, shop.Gold);
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task Concurrent_automatic_requests_consume_one_use_and_do_not_share_character_quota()
    {
        using var shop = Fixture();
        shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,586,4000000000)");
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        await Task.WhenAll(AutoRepair(shop).AsTask(), AutoRepair(shop).AsTask());
        Assert.Equal(1, shop.Scalar("SELECT value FROM character_daily_counters WHERE character_id=1 AND counter_key='premium_auto_repair_used'"));
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Equipment, 12));
        shop.Select(2);
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        await AutoRepair(shop);
        Assert.Equal(1, shop.Scalar("SELECT value FROM character_daily_counters WHERE character_id=2 AND counter_key='premium_auto_repair_used'"));
    }

    private sealed class Prices : IEquipmentRepairDefinitionSource
    {
        public ValueTask<EquipmentRepairDefinition?> GetAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentRepairDefinition?>(itemId switch
            {
                3000 => new(1680, 3, 45),
                100310669 => new(0, 7, 0),
                _ => null,
            });

        public ValueTask<(float CostRate, IReadOnlyList<float> UpgradeRates)?> RatesAsync(CancellationToken ct) =>
            ValueTask.FromResult<(float, IReadOnlyList<float>)?>((0.08415f, new float[] { 1, 2 }));
    }
}
