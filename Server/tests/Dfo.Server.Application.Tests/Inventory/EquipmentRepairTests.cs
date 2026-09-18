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
            (ack.Receipt!.Gold, ack.Receipt.Container, ack.Receipt.SlotIndex));
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
        Assert.Equal(-1, Assert.IsType<RepairItemResponse>(Assert.Single(shop.Sender.Messages)).Receipt!.SlotIndex);
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
        Assert.Equal(99887, Assert.IsType<RepairItemResponse>(Assert.Single(shop.Sender.Messages)).Receipt!.Gold);
    }

    [Fact]
    public async Task Single_nondurable_repair_consumes_zero_and_does_not_write_the_item()
    {
        using var shop = Fixture();
        await shop.SeedCore(20, StackableItemCore.Create(1, 100310669, 4321), InventoryContainerType.Equipment);
        shop.Sql("CREATE TRIGGER refuse_item_write BEFORE INSERT ON item_slots BEGIN SELECT RAISE(ABORT,'unexpected item write'); END;");
        await Repair(shop, InventoryContainerType.Equipment, 20);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Equipment, 20));
        var ack = Assert.IsType<RepairItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Equal((100000, 20), (ack.Receipt!.Gold, ack.Receipt.SlotIndex));
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
        shop.Sql("CREATE TRIGGER refuse_repair BEFORE INSERT ON item_slots BEGIN SELECT RAISE(ABORT,'test repair failure'); END;");
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
            new RepairItemRequest(GameMessageMetadata.Empty, InventoryContainerType.Equipment, 12, -1, 0, 0, 1), default);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Equipment, 12));
        Assert.Null(Assert.IsType<RepairItemResponse>(Assert.Single(shop.Sender.Messages)).Receipt);
    }

    [Fact]
    public async Task Replaced_character_session_cannot_repair_its_old_equipment()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(0), InventoryContainerType.Equipment);
        var old = shop.Session;
        shop.Select(2);
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(
            new RepairItemCommand(shop.OnlineUser, old, InventoryContainerType.Equipment, 12));
        Assert.Equal(EquipmentRepairRejection.NoSession, result.Rejection);
        Assert.Equal(100000, shop.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=1"));
        Assert.Empty(shop.Sender.Messages);
    }

    private static ShopFixture Fixture() => new(services =>
        services.AddSingleton<IEquipmentRepairDefinitionSource>(new Prices()));

    private static InventoryItemCoreState Piece(ushort durability) =>
        StackableItemCore.Create(1, 3000, 1234) with { Durability = durability };

    private static ValueTask Repair(ShopFixture shop, InventoryContainerType container, short slot) =>
        shop.Services.GetRequiredService<RepairItemHandler>().HandleAsync(new(shop.Connection),
            new RepairItemRequest(GameMessageMetadata.Empty, container, slot, -1, 0, 0, 0), default);

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
            ValueTask.FromResult<(float, IReadOnlyList<float>)?>((0.08415f, new float[] { 1 }));
    }
}
