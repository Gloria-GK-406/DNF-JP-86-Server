using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.RepairItem;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.UseStackable;
using Dfo.Server.Application.Commands;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class RepairCouponTests
{
    private const int Coupon = 10093456;

    [Fact]
    public async Task Plain_stackable_use_cannot_consume_a_repair_coupon_without_repairing()
    {
        using var f = Fixture();
        await f.Seed(108, Coupon, 2);
        await f.SeedCore(12, Piece(7), InventoryContainerType.Equipment);
        var result = await f.Services.GetRequiredService<ICommandBus>().SendAsync(
            WholeUse.Command(f.Services, f.Session, new(BusinessInventorySpace.Consumable, 43), Coupon));
        Assert.NotEqual(ItemUseStatus.Used, result.Status);
        Assert.Equal(2, f.Count(Coupon));
        Assert.Equal((ushort)7, await f.Durability(InventoryContainerType.Equipment, 12));
    }

    [Theory]
    [InlineData(108, InventoryContainerType.Equipment, 12, 2)]
    [InlineData(4, InventoryContainerType.Equipment, 12, 1)]
    [InlineData(108, InventoryContainerType.Main, 9, 2)]
    public async Task Coupon_wire_repairs_one_piece_consumes_one_and_persists_before_projection(
        short source, InventoryContainerType container, short target, int count)
    {
        using var f = Fixture();
        await f.Seed(source, Coupon, count);
        await f.SeedCore(target, Piece(7), container);
        f.Sender.BeforeSend = () =>
        {
            Assert.Equal(count - 1, f.Count(Coupon));
            Assert.Equal((ushort)45, f.Durability(container, target).AsTask().GetAwaiter().GetResult());
            Assert.Equal(100000, f.Gold);
        };
        await RepairWire.Send(f, "single", container, target, materialSlot: source);
        Assert.Equal(0, Assert.Single(f.Sender.Messages.OfType<RepairItemResponse>()).ErrorCode);
        var delta = Assert.IsType<InventoryDeltaMessage>(f.Sender.Messages[0]);
        var changed = Assert.Single(delta.Items);
        Assert.Equal(source, changed.SlotIndex);
        Assert.Equal(count == 1 ? -1 : Coupon, changed.Core.ItemId);
        if (count > 1) Assert.Equal(count - 1, changed.Core.Value);
    }

    [Theory]
    [InlineData(false, 7, Coupon, 108, 12, 0)]
    [InlineData(true, 45, Coupon, 108, 12, 0)]
    [InlineData(true, 7, 10093469, 108, 12, 0)]
    [InlineData(true, 7, Coupon, 108, -1, 0)]
    [InlineData(true, 7, Coupon, 108, 12, 1)]
    public async Task Invalid_coupon_requests_never_charge_or_repair(bool dungeon, int durability,
        int item, short source, short target, byte quick)
    {
        using var f = Fixture(dungeon);
        await f.Seed(source, item, 2);
        await f.SeedCore(12, Piece((ushort)durability), InventoryContainerType.Equipment);
        await RepairWire.Send(f, "single", slot: target, materialSlot: source, quick: quick);
        Assert.Equal(2, f.Count(item));
        Assert.Equal((ushort)durability, await f.Durability(InventoryContainerType.Equipment, 12));
        Assert.Equal(100000, f.Gold);
        Assert.NotEqual(0, Assert.Single(f.Sender.Messages.OfType<RepairItemResponse>()).ErrorCode);
    }

    [Fact]
    public async Task Retrying_after_success_cannot_consume_a_second_coupon_on_full_equipment()
    {
        using var f = Fixture();
        await f.Seed(108, Coupon, 2);
        await f.SeedCore(12, Piece(7), InventoryContainerType.Equipment);
        await RepairWire.Send(f, "single", materialSlot: 108);
        await RepairWire.Send(f, "single", materialSlot: 108);
        Assert.Equal(1, f.Count(Coupon));
        Assert.Equal((ushort)45, await f.Durability(InventoryContainerType.Equipment, 12));
        Assert.Equal(new byte[] { 0, 10 }, f.Sender.Messages.OfType<RepairItemResponse>().Select(x => x.ErrorCode));
    }

    [Fact]
    public async Task Replayed_physical_frame_cannot_repair_new_damage_and_spend_again()
    {
        using var f = Fixture();
        await f.Seed(108, Coupon, 2);
        await f.SeedCore(12, Piece(7), InventoryContainerType.Equipment);
        await RepairWire.Send(f, "single", materialSlot: 108, sequence: 7);
        await f.SeedCore(12, Piece(7), InventoryContainerType.Equipment);
        f.Sender.Messages.Clear();
        await RepairWire.Send(f, "single", materialSlot: 108, sequence: 7);
        Assert.Equal(1, f.Count(Coupon));
        Assert.Equal((ushort)7, await f.Durability(InventoryContainerType.Equipment, 12));
        Assert.Empty(f.Sender.Messages);
    }

    [Fact]
    public async Task Failed_equipment_write_rolls_back_coupon_and_sends_no_success()
    {
        using var f = Fixture();
        await f.Seed(108, Coupon, 2);
        await f.SeedCore(12, Piece(7), InventoryContainerType.Equipment);
        f.Sql("CREATE TRIGGER reject_coupon_repair BEFORE UPDATE OF item_core ON items WHEN substr(hex(OLD.item_core),3,8)='B80B0000' BEGIN SELECT RAISE(ABORT,'coupon repair failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => RepairWire.Send(f, "single", materialSlot: 108).AsTask());
        Assert.Equal(2, f.Count(Coupon));
        Assert.Equal((ushort)7, await f.Durability(InventoryContainerType.Equipment, 12));
        Assert.Equal(100000, f.Gold);
        Assert.Empty(f.Sender.Messages);
    }

    [Theory]
    [InlineData(2000, true)]
    [InlineData(4000, false)]
    public async Task Wrong_dungeon_or_unloaded_room_does_not_consume(int dungeonId, bool ready)
    {
        using var f = Fixture(dungeonId: dungeonId, ready: ready);
        await f.Seed(108, Coupon, 2);
        await f.SeedCore(12, Piece(7), InventoryContainerType.Equipment);
        await RepairWire.Send(f, "single", materialSlot: 108);
        Assert.Equal(2, f.Count(Coupon));
        Assert.Equal((ushort)7, await f.Durability(InventoryContainerType.Equipment, 12));
        Assert.NotEqual(0, Assert.Single(f.Sender.Messages.OfType<RepairItemResponse>()).ErrorCode);
    }

    [Fact]
    public async Task Expired_exploration_does_not_consume()
    {
        using var f = Fixture();
        await f.Seed(108, Coupon, 2);
        await f.SeedCore(12, Piece(7), InventoryContainerType.Equipment);
        f.Clock.Advance(TimeSpan.FromMinutes(11));
        await RepairWire.Send(f, "single", materialSlot: 108);
        Assert.Equal(2, f.Count(Coupon));
        Assert.Equal((ushort)7, await f.Durability(InventoryContainerType.Equipment, 12));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Locked_or_expired_coupon_is_not_consumed(bool locked, bool expired)
    {
        using var f = Fixture();
        await f.SeedCore(108, StackableItemCore.Create(2, Coupon, 2) with
        {
            EquipmentLockId = locked ? (byte)1 : (byte)0,
            ExpireTime = expired ? (int)f.Clock.GetUtcNow().AddSeconds(-1).ToUnixTimeSeconds() : 0,
        });
        await f.SeedCore(12, Piece(7), InventoryContainerType.Equipment);
        await RepairWire.Send(f, "single", materialSlot: 108);
        Assert.Equal(2, f.Count(Coupon));
        Assert.Equal((ushort)7, await f.Durability(InventoryContainerType.Equipment, 12));
    }

    [Fact]
    public async Task Failed_coupon_debit_rolls_back_equipment_and_keeps_the_last_coupon()
    {
        using var f = Fixture();
        await f.Seed(108, Coupon, 1);
        await f.SeedCore(12, Piece(7), InventoryContainerType.Equipment);
        f.Sql("CREATE TRIGGER reject_coupon_delete BEFORE DELETE ON item_slots WHEN OLD.slot=108 BEGIN SELECT RAISE(ABORT,'coupon debit failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => RepairWire.Send(f, "single", materialSlot: 108).AsTask());
        Assert.Equal(1, f.Count(Coupon));
        Assert.Equal((ushort)7, await f.Durability(InventoryContainerType.Equipment, 12));
        Assert.Empty(f.Sender.Messages);
    }

    private static InventoryItemCoreState Piece(ushort durability) =>
        StackableItemCore.Create(1, 3000, 1234) with { Durability = durability };

    private static ShopFixture Fixture(bool dungeon = true, int dungeonId = 4000, bool ready = true)
    {
        var f = new ShopFixture(services =>
        {
            services.AddSingleton<IEquipmentRepairDefinitionSource, Prices>();
            services.AddSingleton<IItemUseRuleDefinitionSource, Coupons>();
        });
        if (dungeon) f.InTransaction(() =>
        {
            var cell = new MazeCell(0, 0);
            var layout = new DungeonLayout(dungeonId, 0, 1, 1, cell, cell, 61000, 17,
                new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(61000) })
                { Mode = new DungeonRunMode.LostLand(new(600, 1200, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), [], [])) };
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            var run = runs.PreparePartyEntry([new(f.Session.AccountId, f.Session.CharacterId, 0, true)],
                layout, 0, DungeonCellSeed.CompleteEmpty(cell, 61000), DungeonRunId.New(), f.Clock.GetUtcNow()).Run;
            return true;
        });
        if (dungeon && ready) TestGameplay.Run(f.Services, () =>
        {
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            var run = runs.Find(f.Session.CharacterId)!;
            runs.TryMarkParticipantReady(f.Session.CharacterId, run.RunId, run.RoomVersion);
            Assert.Contains(f.Session.CharacterId, runs.Find(f.Session.CharacterId)!.ReadyParticipants);
        });
        return f;
    }

    private sealed class Coupons : IItemUseRuleDefinitionSource
    {
        public ValueTask<ItemUseRule?> GetAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<ItemUseRule?>(new(true, 0, 0, 1, ItemJobRestriction.Any,
                new(false, [ItemUsePlace.Dungeon]), Action: id == Coupon ? "[repair coupon]" : "[etc]")
                { AllowedDungeonIds = [4000] });
    }

    private sealed class Prices : IEquipmentRepairDefinitionSource
    {
        public ValueTask<EquipmentRepairDefinition?> GetAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentRepairDefinition?>(id == 3000 ? new(1680, 3, 45) : null);
        public ValueTask<(float CostRate, IReadOnlyList<float> UpgradeRates)?> RatesAsync(CancellationToken ct) =>
            ValueTask.FromResult<(float, IReadOnlyList<float>)?>((0.08415f, new float[] { 1, 2 }));
    }
}
