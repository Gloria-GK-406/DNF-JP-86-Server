using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class BusinessEquipmentSwapTests
{
    [Fact]
    public async Task Attribute_update_after_identical_core_swap_keeps_transferred_uid()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop = new ShopFixture();
            var core = StackableItemCore.Create(1, 3000, 101) with { Durability = 4 };
            await shop.SeedCore(9, core);
            await shop.SeedCore(10, core);
            var store = shop.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
                .GetStore(shop.Session.CharacterId, BusinessInventoryStoreKind.Item);
            var container = store.GetContainer(BusinessInventorySpace.CarriedEquipment);
            var uid0 = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9");
            var uid1 = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=10");
            var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using (var tx = scope.Begin())
            {
                var first = await container.TakeAsync((await container.GetAsync(0, default))!, null, default);
                var second = await container.TakeAsync((await container.GetAsync(1, default))!, null, default);
                var landed = await container.PutAsync(1, first, default);
                await container.PutAsync(0, second, default);
                await container.UpdateAsync(landed, x => x with { Durability = 8 }, default);
                await tx.CommitAsync(default);
            }

            Assert.Equal(uid1, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9"));
            Assert.Equal(uid0, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=10"));
            Assert.Equal((ushort)8, (await container.GetAsync(1, default))!.Snapshot.ToCore().Durability);
        });
    }

    [SkippableFact]
    public async Task Wear_swap_of_identical_cores_preserves_both_distinct_uids()
    {
        Skip.If(new Dfo.Server.Infrastructure.Content.PvfContentOptions().ResolveArchivePath() is null,
            "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var core = StackableItemCore.Create(1, 10018, 101) with { Durability = 4 };
        await shop.Host.SeedCore(9, core);
        await shop.Host.SeedCore(14, core, InventoryContainerType.Equipment);
        var uidBag = shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9");
        var uidWorn = shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=3 AND slot_index=14");
        Assert.NotEqual(uidBag, uidWorn);

        var moved = await InventoryMoveTestAccess.MoveAsync(shop.Host.Services,
            new InventoryMoveRequest(shop.Host.Session.CharacterId,
                new(BusinessInventorySpace.CarriedEquipment, 0), 1,
                new(BusinessInventorySpace.WornEquipment, 2)));

        Assert.Equal(ItemMoveStatus.Moved, moved.Status);
        Assert.Equal(uidWorn, shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9"));
        Assert.Equal(uidBag, shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=3 AND slot_index=14"));
    }

    [SkippableFact]
    public async Task Two_wear_moves_in_one_transaction_keep_the_original_instance_through_the_chain()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.If(new Dfo.Server.Infrastructure.Content.PvfContentOptions().ResolveArchivePath() is null,
                "PVF_ARCHIVE_PATH is not available.");
            using var shop = new CeraShopFixture(useArchive: true);
            var core = StackableItemCore.Create(1, 10018, 101) with { Durability = 4 };
            await shop.Host.SeedCore(9, core);
            await shop.Host.SeedCore(14, core, InventoryContainerType.Equipment);
            var uidBag = shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9");
            var uidWorn = shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=3 AND slot_index=14");
            var handler = new InventoryMoveDomainService(
                shop.Host.Services.GetRequiredService<IBusinessInventorySlotDomainService>(),
                shop.Host.Services.GetRequiredService<IItemMovementDomainService>(), creatures: TestRequired.Absent<Dfo.Server.Abstractions.Creatures.ICreatureRuntimeDomainService>(), sessions: TestRequired.Absent<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>(), cargo: TestRequired.Absent<Dfo.Server.Abstractions.Inventory.ICargoTransferDomainService>());
            var scope = (DomainTransactionScope)shop.Host.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using (var tx = scope.Begin())
            {
                var context = DomainCommandFixture.Context(Guid.NewGuid(), typeof(InventoryMoveRequest),
                    typeof(InventoryMoveDomainService), FixtureUnitKind.Transactional);
                var putOn = await handler.MoveAsync( new(shop.Host.Session.CharacterId,
                    new(BusinessInventorySpace.CarriedEquipment, 0), 1,
                    new(BusinessInventorySpace.WornEquipment, 2)), default);
                Assert.Equal(ItemMoveStatus.Moved, putOn.Status);
                var takeOff = await handler.MoveAsync( new(shop.Host.Session.CharacterId,
                    new(BusinessInventorySpace.WornEquipment, 2), 1,
                    new(BusinessInventorySpace.CarriedEquipment, 1)), default);
                Assert.Equal(ItemMoveStatus.Moved, takeOff.Status);
                await tx.CommitAsync(default);
            }

            Assert.Equal(uidWorn, shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9"));
            Assert.Equal(uidBag, shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=10"));
            Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=3 AND slot_index=14"));
        });
    }

    [Fact]
    public async Task Same_core_equipment_exchanges_distinct_uids()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop = new ShopFixture();
            var sameCore = StackableItemCore.Create(1, 3000, 101) with { Durability = 4 };
            await shop.SeedCore(9, sameCore);
            await shop.SeedCore(10, sameCore);
            var store = shop.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
                .GetStore(shop.Session.CharacterId, BusinessInventoryStoreKind.Item);
            var container = store.GetContainer(BusinessInventorySpace.CarriedEquipment);
            var uid0 = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9");
            var uid1 = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=10");
            Assert.NotEqual(uid0, uid1);

            var mover = shop.Services.GetRequiredService<IItemMovementDomainService>();
            var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using (var tx = scope.Begin())
            {
                var moved = await mover.MoveWithinBackingAsync(
                    new(new(BusinessInventorySpace.CarriedEquipment, 0), container),
                    new(new(BusinessInventorySpace.CarriedEquipment, 1), container), 1, default);
                Assert.Equal(new ItemMoveOutcome.Moved(1, 3000), moved);
                await tx.CommitAsync(default);
            }

            Assert.Equal(uid1, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9"));
            Assert.Equal(uid0, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=10"));
        });
    }

    [Fact]
    public async Task Same_template_equipment_exchanges_both_instances_without_merging()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop = new ShopFixture();
            await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 101) with { Durability = 4 });
            await shop.SeedCore(10, StackableItemCore.Create(1, 3000, 202) with { Durability = 8 });
            var store = shop.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
                .GetStore(shop.Session.CharacterId, BusinessInventoryStoreKind.Item);
            var container = store.GetContainer(BusinessInventorySpace.CarriedEquipment);
            var before0 = (await container.GetAsync(0, default))!;
            var before1 = (await container.GetAsync(1, default))!;
            var uid0 = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9");
            var uid1 = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=10");
            Assert.NotEqual(uid0, uid1);

            var mover = shop.Services.GetRequiredService<IItemMovementDomainService>();
            var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using (var tx = scope.Begin())
            {
                var moved = await mover.MoveWithinBackingAsync(
                    new(new(BusinessInventorySpace.CarriedEquipment, 0), container),
                    new(new(BusinessInventorySpace.CarriedEquipment, 1), container), 1, default);
                Assert.Equal(new ItemMoveOutcome.Moved(1, 3000), moved);
                await tx.CommitAsync(default);
            }

            var after0 = (await container.GetAsync(0, default))!;
            var after1 = (await container.GetAsync(1, default))!;
            Assert.Equal(uid1, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9"));
            Assert.Equal(uid0, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=10"));
            Assert.Equal(before1.Snapshot.ToCore().Value, after0.Snapshot.ToCore().Value);
            Assert.Equal(before0.Snapshot.ToCore().Value, after1.Snapshot.ToCore().Value);
            Assert.Equal((ushort)8, after0.Snapshot.ToCore().Durability);
            Assert.Equal((ushort)4, after1.Snapshot.ToCore().Durability);
        });
    }
}
