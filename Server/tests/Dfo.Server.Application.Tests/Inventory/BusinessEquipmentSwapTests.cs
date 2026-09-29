using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class BusinessEquipmentSwapTests
{
    [Fact]
    public async Task Attribute_update_after_identical_core_swap_keeps_transferred_uid()
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
            await container.PutTransferredAsync(1, core, container, 0, default);
            await container.PutTransferredAsync(0, core, container, 1, default);
            await container.PutAsync(1, core with { Durability = 8 }, default);
            await tx.CommitAsync(default);
        }

        Assert.Equal(uid1, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9"));
        Assert.Equal(uid0, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=10"));
        Assert.Equal((ushort)8, (await container.ReadSlotAsync(1, default))!.Core.Durability);
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

        var moved = await shop.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new MoveItemSpaceCommand(shop.Host.Session.CharacterId,
                new(BusinessInventorySpace.CarriedEquipment, 0), 1,
                new(BusinessInventorySpace.WornEquipment, 2)));

        Assert.Equal(ItemMoveStatus.Moved, moved.Status);
        Assert.Equal(uidWorn, shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9"));
        Assert.Equal(uidBag, shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=3 AND slot_index=14"));
    }

    [SkippableFact]
    public async Task Two_wear_moves_in_one_transaction_keep_the_original_instance_through_the_chain()
    {
        Skip.If(new Dfo.Server.Infrastructure.Content.PvfContentOptions().ResolveArchivePath() is null,
            "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var core = StackableItemCore.Create(1, 10018, 101) with { Durability = 4 };
        await shop.Host.SeedCore(9, core);
        await shop.Host.SeedCore(14, core, InventoryContainerType.Equipment);
        var uidBag = shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9");
        var uidWorn = shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=3 AND slot_index=14");
        var handler = new MoveItemSpaceCommandHandler(
            shop.Host.Services.GetRequiredService<IBusinessInventorySlotDomainService>(),
            shop.Host.Services.GetRequiredService<IItemMovementDomainService>());
        var scope = (DomainTransactionScope)shop.Host.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using (var tx = scope.Begin())
        {
            var context = new CommandExecutionContext(Guid.NewGuid(), typeof(MoveItemSpaceCommand),
                typeof(MoveItemSpaceCommandHandler), CommandExecutionKind.Transactional);
            var putOn = await handler.HandleAsync(context, new(shop.Host.Session.CharacterId,
                new(BusinessInventorySpace.CarriedEquipment, 0), 1,
                new(BusinessInventorySpace.WornEquipment, 2)), default);
            Assert.Equal(ItemMoveStatus.Moved, putOn.Status);
            var takeOff = await handler.HandleAsync(context, new(shop.Host.Session.CharacterId,
                new(BusinessInventorySpace.WornEquipment, 2), 1,
                new(BusinessInventorySpace.CarriedEquipment, 1)), default);
            Assert.Equal(ItemMoveStatus.Moved, takeOff.Status);
            await tx.CommitAsync(default);
        }

        Assert.Equal(uidWorn, shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9"));
        Assert.Equal(uidBag, shop.Host.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=10"));
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=3 AND slot_index=14"));
    }

    [Fact]
    public async Task Same_core_equipment_exchanges_distinct_uids()
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
    }

    [Fact]
    public async Task Same_template_equipment_exchanges_both_instances_without_merging()
    {
        using var shop = new ShopFixture();
        await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 101) with { Durability = 4 });
        await shop.SeedCore(10, StackableItemCore.Create(1, 3000, 202) with { Durability = 8 });
        var store = shop.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetStore(shop.Session.CharacterId, BusinessInventoryStoreKind.Item);
        var container = store.GetContainer(BusinessInventorySpace.CarriedEquipment);
        var before0 = (await container.ReadSlotAsync(0, default))!;
        var before1 = (await container.ReadSlotAsync(1, default))!;
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

        var after0 = (await container.ReadSlotAsync(0, default))!;
        var after1 = (await container.ReadSlotAsync(1, default))!;
        Assert.Equal(uid1, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9"));
        Assert.Equal(uid0, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=10"));
        Assert.Equal(before1.Core.Value, after0.Core.Value);
        Assert.Equal(before0.Core.Value, after1.Core.Value);
        Assert.Equal((ushort)8, after0.Core.Durability);
        Assert.Equal((ushort)4, after1.Core.Durability);
    }
}
