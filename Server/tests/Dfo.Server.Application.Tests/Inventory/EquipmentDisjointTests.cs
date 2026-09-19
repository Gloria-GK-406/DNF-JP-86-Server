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
            (response.Receipt!.TargetSlotIndex, response.Receipt.ItemSpace));
        Assert.Equal(new EquipmentDisjointMaterialReceipt(121, 1000, 12),
            Assert.Single(response.Receipt.Materials));
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
        Assert.Null(response.Receipt);
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
    public async Task Opaque_client_context_does_not_reject_system_disjoint()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());

        await Disjoint(shop, 9, contextValue: 0x12345678);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(12, shop.Count(1000));
        Assert.NotNull(Assert.IsType<DisjointItemResponse>(
            Assert.Single(shop.Sender.Messages)).Receipt);
    }

    private static ShopFixture Fixture() => new(services =>
        services.AddSingleton<IEquipmentDisjointRuleService>(new FixedRules()));

    private static InventoryItemCoreState Piece() =>
        StackableItemCore.Create(1, 3000, 1234) with { Durability = 45 };

    private static ValueTask Disjoint(ShopFixture shop, short slot, int contextValue = 0) =>
        shop.Services.GetRequiredService<DisjointItemHandler>().HandleAsync(new(shop.Connection),
            new DisjointItemRequest(GameMessageMetadata.Empty, slot, InventoryContainerType.Main,
                -1, contextValue), default);

    private sealed class FixedRules : IEquipmentDisjointRuleService
    {
        public ValueTask<EquipmentDisjointRuleResult> ResolveAsync(EquipmentDisjointCandidate equipment,
            EquipmentDisjointSettings settings, int randomSeed, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new EquipmentDisjointRuleResult(
                EquipmentDisjointRejection.None, [new(1000, 12)]));
    }
}
