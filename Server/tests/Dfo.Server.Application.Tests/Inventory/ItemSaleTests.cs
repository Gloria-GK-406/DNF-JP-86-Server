using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.SellItem;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ItemSaleTests
{
    [Theory]
    [InlineData(true, true, true, 1000, true)]
    [InlineData(true, false, true, 1000, false)]
    [InlineData(false, true, true, 1000, false)]
    [InlineData(true, true, false, 1000, false)]
    [InlineData(true, true, true, 2, false)]
    public async Task Dungeon_shop_requires_current_clear_boss_and_matching_participant(
        bool boss, bool open, bool owner, int npc, bool accepted)
    {
        using var shop = Fixture();
        shop.Content.Shops[1000] = new(1000, 1000, [new(null, [5000])]) { VisibleOnDungeonClear = true };
        await shop.Seed(65, 5000, 2);
        var cell = new MazeCell(0, 0); var other = new MazeCell(1, 0);
        var layout = new DungeonLayout(144, 0, boss ? (byte)1 : (byte)2, 1, cell, boss ? cell : other, 55611, 0,
            boss ? new Dictionary<MazeCell, int> { [cell] = 55611 }
                : new Dictionary<MazeCell, int> { [cell] = 55611, [other] = 55611 });
        var runs = shop.Services.GetRequiredService<IDungeonRunDomainService>();
        var run = runs.PreparePartyEntry([new(owner ? shop.Session.AccountId : new(999),
            shop.Session.CharacterId, 0, true)], layout, 0).Run;
        if (open) runs.TryOpenClear(shop.Session.CharacterId, run.RunId, run.RoomVersion);
        await Sell(shop, 65, 1, npc: npc);
        Assert.Equal(accepted ? 1 : 2, shop.Count(5000));
        Assert.Equal(accepted ? 100031 : 100000, shop.Gold);
        Assert.Equal(accepted, Assert.IsType<SellItemResponse>(Assert.Single(shop.Sender.Messages)).Slot is not null);
        runs.End(shop.Session.CharacterId, run.RunId);
        await Sell(shop, 65, 1, npc: 1000);
        Assert.Equal(accepted ? 1 : 2, shop.Count(5000));
    }

    [Fact]
    public async Task Dungeon_only_shop_cannot_be_used_from_town()
    {
        using var shop = Fixture();
        shop.Content.Shops[1000] = new(1000, 1000, [new(null, [5000])]) { VisibleOnDungeonClear = true };
        await shop.Seed(65, 5000, 2);
        await Sell(shop, 65, 1, npc: 1000);
        Assert.Equal(2, shop.Count(5000));
        Assert.Equal(100000, shop.Gold);
    }
    [Theory]
    [InlineData(1, 9, 100031)]
    [InlineData(10, 0, 100310)]
    public async Task Selling_a_pile_commits_the_exact_count_and_price_before_its_single_ack(int count, int left, int gold)
    {
        using var shop = Fixture();
        await shop.Seed(65, 5000, 10);
        shop.Sender.BeforeSend = () => { Assert.Equal(left, shop.Count(5000)); Assert.Equal(gold, shop.Gold); };
        await Sell(shop, 65, count);
        var response = Assert.IsType<SellItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.NotNull(response.Slot);
        Assert.Equal((65, count, gold), (response.Slot.Value.Index, response.Count, response.Gold));
    }

    [Fact]
    public async Task An_equipment_instance_is_one_piece_and_is_not_counted_by_its_identity()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 178234) with { Durability = 45 });
        await Sell(shop, 9, 1);
        Assert.Equal(102332, shop.Gold);
        Assert.Equal(0, shop.Count(3000));
        await Sell(shop, 9, 1);
        Assert.Equal(102332, shop.Gold);
        Assert.Equal(17, Assert.IsType<SellItemResponse>(shop.Sender.Messages[1]).ErrorCode);
    }

    [Theory]
    [InlineData(0, 65, 0, 2)]
    [InlineData(0, 65, -1, 2)]
    [InlineData(0, 65, 11, 2)]
    [InlineData(0, 65, 1, 999)]
    public async Task Invalid_quantity_container_slot_or_npc_cannot_change_the_inventory(byte container, short slot, short count, int npc)
    {
        using var shop = Fixture();
        await shop.Seed(65, 5000, 10);
        await Sell(shop, slot, count, (InventoryContainerType)container, npc);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(10, shop.Count(5000));
        Assert.Null(Assert.IsType<SellItemResponse>(Assert.Single(shop.Sender.Messages)).Slot);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 365)]
    [InlineData(12, 120)]
    [InlineData(255, 65)]
    public async Task An_unmappable_game_slot_is_a_boundary_failure_and_cannot_reach_the_command(
        byte container, short slot)
    {
        using var shop = Fixture();
        await shop.Seed(65, 5000, 10);

        await Assert.ThrowsAsync<InventorySlotMappingException>(() =>
            Sell(shop, slot, 1, (InventoryContainerType)container).AsTask());

        Assert.Equal(100000, shop.Gold);
        Assert.Equal(10, shop.Count(5000));
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task A_bad_field_sum_and_an_old_session_cannot_sell()
    {
        using var shop = Fixture();
        await shop.Seed(65, 5000, 10);
        await Sell(shop, 65, 1, sum: 0);
        var old = shop.Session;
        shop.Select(2);
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(new SellItemCommand(
            shop.OnlineUser, old,
            new BusinessSlot(BusinessInventorySpace.Consumable, 0), 1, 2));
        Assert.Equal(ItemSaleRejection.NoSession, result.Rejection);
        Assert.Equal(10, shop.Scalar("SELECT count FROM item_slots WHERE owner_id=1 AND list_type=0 AND slot_index=65"));
        Assert.Equal(100000, shop.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=1"));
    }

    [Fact]
    public async Task A_locked_piece_is_retained()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 1234) with { EquipmentLockId = 1 });
        await Sell(shop, 9, 1);
        Assert.Equal(1, shop.Count(3000));
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(0xd6, Assert.IsType<SellItemResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public async Task A_full_or_partly_full_gold_limit_refuses_the_whole_sale(int space)
    {
        using var shop = Fixture(100000 + space);
        await shop.Seed(65, 5000, 10);
        await Sell(shop, 65, 1);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(10, shop.Count(5000));
        Assert.Equal(22, Assert.IsType<SellItemResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task A_storage_failure_rolls_back_the_gold_and_item_and_publishes_nothing(int count)
    {
        using var shop = Fixture();
        await shop.Seed(65, 5000, 10);
        shop.Sql("CREATE TRIGGER refuse_sale BEFORE DELETE ON item_slots BEGIN SELECT RAISE(ABORT,'test sale failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Sell(shop, 65, count).AsTask());
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(10, shop.Count(5000));
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task Two_concurrent_requests_cannot_sell_the_same_piece_twice()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 1111) with { Durability = 45 });
        await Task.WhenAll(Sell(shop, 9, 1).AsTask(), Sell(shop, 9, 1).AsTask());
        Assert.Equal(102332, shop.Gold);
        Assert.Single(shop.Sender.Messages.OfType<SellItemResponse>(), x => x.Slot is not null);
    }

    [Fact]
    public async Task Failed_delivery_does_not_retry_or_reverse_the_sale()
    {
        using var shop = Fixture();
        await shop.Seed(65, 5000, 10);
        shop.Sender.BeforeSend = () => throw new IOException("test connection failure");
        await Sell(shop, 65, 1);
        Assert.Equal(9, shop.Count(5000));
        Assert.Equal(100031, shop.Gold);
        Assert.Equal(1, shop.Sender.Attempts);
    }

    [Fact]
    public async Task An_event_for_the_old_character_is_not_sent_to_the_new_selection()
    {
        using var shop = Fixture();
        await shop.Seed(65, 5000, 10);
        await Sell(shop, 65, 1);
        var receipt = new ItemSaleReceipt(
            shop.Session,
            new BusinessSlot(BusinessInventorySpace.Consumable, 0),
            5000, 1, 31, null, DateTimeOffset.UtcNow);
        shop.Select(2);
        await shop.Services.GetRequiredService<INotificationBus>().PublishAsync(
            new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]), new ItemSaleCompleted(receipt), default);
        Assert.Single(shop.Sender.Messages);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 51)]
    [InlineData(44, 2280)]
    [InlineData(45, 2332)]
    [InlineData(60, 2332)]
    public async Task Equipment_sale_applies_durability_then_truncates_then_applies_the_equipment_discount(ushort durability, int price)
    {
        using var shop = Fixture();
        await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 1111) with { Durability = durability });
        await Sell(shop, 9, 1);
        Assert.Equal(100000 + price, shop.Gold);
        Assert.Equal(0, shop.Count(3000));
    }

    [Fact]
    public async Task Equipment_with_an_unverified_special_price_branch_is_retained()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 1111) with { Durability = 45, RandomOptionState = 1 });
        await Sell(shop, 9, 1);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(1, shop.Count(3000));
        Assert.Null(Assert.IsType<SellItemResponse>(Assert.Single(shop.Sender.Messages)).Slot);
    }

    private static ValueTask Sell(ShopFixture shop, int slot, int count,
        InventoryContainerType container = InventoryContainerType.Main, int npc = 2, int? sum = null) =>
        shop.Services.GetRequiredService<SellItemHandler>().HandleAsync(new(shop.Connection),
            new SellItemRequest(
                GameMessageMetadata.Empty,
                new GameSlot(container, slot),
                checked((short)count), npc, sum ?? (byte)container + slot + count),
            default);

    private static ShopFixture Fixture(int cap = 1000000) => new(services =>
    {
        services.AddSingleton<IItemSaleDefinitionSource>(new Prices());
        services.AddSingleton<IGoldCarryLimitSource>(new GoldLimit(cap));
    });

    private sealed class Prices : IItemSaleDefinitionSource
    {
        public ValueTask<ItemSaleDefinition?> GetAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<ItemSaleDefinition?>(itemId switch
            {
                3000 => new(12960, 200, true, true, 45),
                5000 => new(155, 200, true),
                _ => null,
            });
    }
    private sealed class GoldLimit(int cap) : IGoldCarryLimitSource
    {
        public int BaseLimitFor(int level) => cap;
    }
}
