using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DisjointItem;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentDisjointTests
{
    [Fact]
    public async Task One_transaction_removes_the_piece_grants_materials_and_then_acknowledges()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        shop.Sender.BeforeSend = () =>
        {
            Assert.Null(shop.Durability(InventoryContainerType.Main, 9)
                .AsTask().GetAwaiter().GetResult());
            Assert.Equal(12, shop.Count(1000));
        };

        await Disjoint(shop, 9);

        var response = Assert.IsType<DisjointItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Equal((9, InventoryContainerType.Main),
            (response.TargetSlot!.Value.Index, response.TargetSlot.Value.Space));
        Assert.Equal(new DisjointItemMaterial(
            new GameSlot(InventoryContainerType.Main, 121), 1000, 12),
            Assert.Single(response.Materials!));
    }

    [Fact]
    public async Task Full_material_band_keeps_the_source_and_returns_inventory_full()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 1000, 3);
        await shop.Seed(122, 1000, 1000, 3);
        for (var slot = 3; slot <= 8; slot++)
            await shop.Seed(slot, 2000, 10);

        await Disjoint(shop, 9);

        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(2000, shop.Count(1000));
        var response = Assert.IsType<DisjointItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Null(response.TargetSlot);
        Assert.Equal(0x04, response.ErrorCode);
    }

    [Fact]
    public async Task Failed_material_commit_rolls_back_the_source_and_emits_no_ack()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        shop.Sql("CREATE TRIGGER refuse_disjoint_reward BEFORE INSERT ON item_slots WHEN NEW.id = 1000 BEGIN SELECT RAISE(ABORT,'forced disjoint failure'); END;");

        await Assert.ThrowsAsync<SqliteException>(() => Disjoint(shop, 9).AsTask());

        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(0, shop.Count(1000));
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task Batch_ui_sequence_is_two_independent_single_item_transactions()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.SeedCore(10, Piece());

        await Disjoint(shop, 9);
        await Disjoint(shop, 10);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Null(await shop.Durability(InventoryContainerType.Main, 10));
        Assert.Equal(24, shop.Count(1000));
        Assert.Equal(2, shop.Sender.Messages.Count);
    }

    [Fact]
    public async Task Portable_disjoint_consumes_one_tool_and_settles_the_piece_together()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(65, 2600469, 2);

        await Disjoint(shop, 9, disjointToolSlot: 65);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(1, shop.Count(2600469));
        Assert.Equal(12, shop.Count(1000));
        Assert.NotNull(Assert.IsType<DisjointItemResponse>(
            Assert.Single(shop.Sender.Messages)).TargetSlot);
    }

    [Fact]
    public async Task Portable_disjoint_rolls_back_tool_piece_and_rewards_when_settlement_fails()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(65, 2600469, 2);
        shop.Sql("CREATE TRIGGER refuse_portable_reward BEFORE INSERT ON item_slots "
            + "WHEN NEW.id = 1000 BEGIN SELECT RAISE(ABORT,'forced portable failure'); END;");

        await Assert.ThrowsAsync<SqliteException>(() =>
            Disjoint(shop, 9, disjointToolSlot: 65).AsTask());

        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(2, shop.Count(2600469));
        Assert.Equal(0, shop.Count(1000));
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task Portable_batch_consumes_one_tool_for_each_independent_target()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.SeedCore(10, Piece());
        await shop.Seed(65, 2600469, 3);

        await Disjoint(shop, 9, disjointToolSlot: 65);
        await Disjoint(shop, 10, disjointToolSlot: 65);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Null(await shop.Durability(InventoryContainerType.Main, 10));
        Assert.Equal(1, shop.Count(2600469));
        Assert.Equal(24, shop.Count(1000));
        Assert.Equal(2, shop.Sender.Messages.Count);
    }

    [Fact]
    public async Task Portable_machine_level_limit_rejects_without_consuming_tool_or_piece()
    {
        using var shop = Fixture(equipmentLevel: 60);
        await shop.SeedCore(9, Piece());
        await shop.Seed(65, 2600469, 2);

        await Disjoint(shop, 9, disjointToolSlot: 65);

        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(2, shop.Count(2600469));
        Assert.Equal(0, shop.Count(1000));
        var response = Assert.IsType<DisjointItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Null(response.TargetSlot);
        Assert.Equal(0x13, response.ErrorCode);
    }

    [Fact]
    public async Task Reported_provider_id_does_not_reject_system_disjoint()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());

        await Disjoint(shop, 9, disjointProviderId: 0x12345678);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(12, shop.Count(1000));
        Assert.NotNull(Assert.IsType<DisjointItemResponse>(
            Assert.Single(shop.Sender.Messages)).TargetSlot);
    }

    [Fact]
    public async Task Equipment_in_a_quick_slot_passes_occupancy_then_disjoints_by_equipment_behavior()
    {
        using var shop = Fixture();
        await shop.SeedCore(4, Piece());

        await Disjoint(shop, 4);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 4));
        var response = Assert.IsType<DisjointItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Equal(new GameSlot(InventoryContainerType.Main, 4), response.TargetSlot);
    }

    [Fact]
    public async Task A_non_equipment_in_a_quick_slot_passes_occupancy_but_behavior_refuses_disjoint()
    {
        using var shop = Fixture();
        await shop.Seed(4, 4000, 1);

        await Disjoint(shop, 4);

        Assert.Equal(1, shop.Count(4000));
        var response = Assert.IsType<DisjointItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Null(response.TargetSlot);
        Assert.Equal(0x13, response.ErrorCode);
    }

    [Fact]
    public async Task Stored_content_that_contradicts_its_business_space_is_an_invariant_failure()
    {
        using var shop = Fixture();
        await shop.SeedCore(121, Piece());

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Disjoint(shop, 121).AsTask());

        Assert.Contains("Material/0", failure.Message, StringComparison.Ordinal);
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 121));
        Assert.Empty(shop.Sender.Messages);
    }

    private static ShopFixture Fixture(int equipmentLevel = 10) => new(services =>
    {
        services.AddSingleton<IEquipmentDisjointRuleService>(new FixedRules());
        services.AddSingleton<IEquipmentDisjointDefinitionSource>(
            new DisjointContent(equipmentLevel));
    });

    private static InventoryItemCoreState Piece() =>
        StackableItemCore.Create(1, 3000, 1234) with { Durability = 45 };

    private static ValueTask Disjoint(
        ShopFixture shop,
        short slot,
        int disjointProviderId = 0,
        short disjointToolSlot = -1) =>
        shop.Services.GetRequiredService<DisjointItemHandler>().HandleAsync(new(shop.Connection),
            new DisjointItemRequest(
                GameMessageMetadata.Empty,
                new GameSlot(InventoryContainerType.Main, slot), disjointToolSlot, disjointProviderId),
            default);

    private sealed class FixedRules : IEquipmentDisjointRuleService
    {
        public ValueTask<EquipmentDisjointRuleResult> ResolveAsync(EquipmentDisjointCandidate equipment,
            EquipmentDisjointSettings settings, int randomSeed, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new EquipmentDisjointRuleResult(
                EquipmentDisjointRejection.None, [new(1000, 12)]));
    }

    private sealed class DisjointContent(int equipmentLevel) : IEquipmentDisjointDefinitionSource
    {
        public ValueTask<EquipmentDisjointDefinition?> GetEquipmentAsync(
            int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<EquipmentDisjointDefinition?>(
                itemId == 3000 ? new(100, equipmentLevel, 1, true) : null);

        public ValueTask<PortableDisjointMachineDefinition?> GetPortableMachineAsync(
            int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<PortableDisjointMachineDefinition?>(itemId switch
            {
                2600468 => new(0, 30),
                2600469 => new(1, 50),
                2600470 => new(2, 70),
                2600548 => new(3, 90),
                _ => null,
            });

        public ValueTask<EquipmentDisjointTable?> GetSystemTableAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<EquipmentDisjointTable?>(null);
    }
}
