using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ContainerAdditionTests
{
    [Fact]
    public async Task First_reward_in_an_empty_ordinary_slot_creates_and_commits_one_pile()
    {
        using var f = Fixture();
        var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        var scope = (Dfo.Server.Application.Transactions.DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>();
        Assert.Equal(0, f.Count(9001));
        await using (var tx = scope.Begin())
        {
            var reward = await f.Services.GetRequiredService<IItemGrantDomainService>()
                .GrantAsync(new(1), new(1), containers, 9001, 2, default);
            var added = Assert.IsType<BusinessItemAdditionResult>(reward);
            Assert.Equal(new ItemPlacementOutcome.PutInEmptySlot(0, 2), added.Placement);
            Assert.Equal(BusinessInventorySpace.Material, added.Container.Space);
            var reference = (await added.Container.GetAsync(0, default))!;
            Assert.True(reference.IsPile);
            Assert.Null(reference.ItemUid);
            Assert.Equal(2, reference.Count);
            await tx.CommitAsync(default);
        }
        Assert.Equal(2, f.Count(9001));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=121"));
    }

    [Fact]
    public async Task Routes_material_and_pet_consumable_without_a_caller_selected_bag()
    {
        using var f = Fixture();
        var service = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        var material = await service.AddAsync(new(1), 9001, 5, default);
        Assert.Equal(new[] { 0, 1, 2 }, material.Items.Select(x => x.Slot.Index));
        Assert.Equal(new[] { 2, 2, 1 }, material.Items.Select(x => x.Count));
        Assert.All(material.Items, x => Assert.Equal(BusinessInventorySpace.Material, x.Slot.Space));
        var pet = await service.AddAsync(new(1), 9002, 1, default);
        Assert.Equal(BusinessInventorySpace.CreatureConsumable, Assert.Single(pet.Items).Slot.Space);
        Assert.Equal(0, pet.Items[0].Slot.Index);
        Assert.Equal(5, f.Count(9001));
    }

    [Fact]
    public async Task Two_business_containers_can_both_hold_local_slot_zero()
    {
        using var f = Fixture();
        var grants = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        grants.Register(new(1), new PersistentItemContainer(
            ContainerId.ForCharacter(new(1), InventoryContainerType.Pet),
            new(InventoryContainerType.Pet, 0), f.Services.GetRequiredService<IItemDomainStore>(),
            f.Services.GetRequiredService<IDomainTransactionAccessor>(),
            f.Services.GetRequiredService<IItemPlacementRuleService>(),
            f.Services.GetRequiredService<IItemSlotRuleService>()));
        await grants.AddAsync(new(1), 9001, 1, default);
        await grants.AddAsync(new(1), 9002, 1, default);

        var stores = f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>();
        var item = stores.GetStore(new CharacterId(1), BusinessInventoryStoreKind.Item)
            .GetContainer(BusinessInventorySpace.Material);
        var pet = stores.GetStore(new CharacterId(1), BusinessInventoryStoreKind.Creature)
            .GetContainer(BusinessInventorySpace.CreatureConsumable);
        Assert.Equal(9001, (await item.GetAsync(0, default))!.ItemId);
        Assert.Equal(9002, (await pet.GetAsync(0, default))!.ItemId);
    }

    [Fact]
    public async Task Grant_routes_pet_consumable_to_pet_store_even_when_main_is_available()
    {
        using var f = Fixture();
        var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        containers.Register(new(1), new PersistentItemContainer(
            ContainerId.ForCharacter(new(1), InventoryContainerType.Pet),
            new(InventoryContainerType.Pet, 0), f.Services.GetRequiredService<IItemDomainStore>(),
            f.Services.GetRequiredService<IDomainTransactionAccessor>(),
            f.Services.GetRequiredService<IItemPlacementRuleService>(),
            f.Services.GetRequiredService<IItemSlotRuleService>()));
        var scope = (Dfo.Server.Application.Transactions.DomainTransactionScope)
            f.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var lease = scope.Begin();
        var outcome = await f.Services.GetRequiredService<IItemGrantDomainService>()
            .GrantAsync(new(1), new(1), containers, 9002, 1, default);
        await lease.CommitAsync(default);

        var item = Assert.IsType<BusinessItemAdditionResult>(outcome);
        Assert.Equal(BusinessInventorySpace.CreatureConsumable, item.Container.Space);
        Assert.Equal(0, Assert.IsType<ItemPlacementOutcome.PutInEmptySlot>(item.Placement).SlotIndex);
        Assert.Equal(9002, (await item.Container.GetAsync(0, default))!.ItemId);
        Assert.Equal(0, f.Count(9001));
    }

    [Fact]
    public async Task Whole_quantity_rolls_back_when_only_part_fits_and_unknown_items_throw()
    {
        using var f = Fixture();
        var service = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        var full = await Assert.ThrowsAsync<ContainerAdditionException>(async () =>
            await service.AddAsync(new(1), 9001, 19, default));
        Assert.Equal(ContainerAdditionFailure.NoRoom, full.Reason);
        Assert.Equal(0, f.Count(9001));
        var unknown = await Assert.ThrowsAsync<ContainerAdditionException>(async () =>
            await service.AddAsync(new(1), 999999, 1, default));
        Assert.Equal(ContainerAdditionFailure.UnknownItem, unknown.Reason);
    }

    [Fact]
    public async Task Account_material_routing_does_not_require_a_character_item_placement()
    {
        using var f = Fixture();
        var service = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        var addition = await service.AddAsync(new(1), 3037, 3, default);
        var item = Assert.Single(addition.Items);
        Assert.Equal(new BusinessSlot(BusinessInventorySpace.SharedMaterial, 4), item.Slot);
        Assert.Equal(3, item.Count);
        Assert.Equal(0, f.Count(3037));
        Assert.Equal(3, f.SharedCount(3037, 1));
    }

    [Fact]
    public async Task Misregistered_persistent_container_cannot_write_another_characters_bag()
    {
        using var f = Fixture();
        var service = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        var wrong = new PersistentItemContainer(ContainerId.ForCharacter(new(2), InventoryContainerType.Main), new(InventoryContainerType.Main, 0), f.Services.GetRequiredService<IItemDomainStore>(),
            f.Services.GetRequiredService<IDomainTransactionAccessor>(),
            f.Services.GetRequiredService<IItemPlacementRuleService>(),
            f.Services.GetRequiredService<IItemSlotRuleService>());
        service.Register(new(1), wrong);
        service.UseContainer(new(1), wrong.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.AddAsync(new(1), 9001, 1, default));
        Assert.Equal(0, f.Scalar("SELECT count(*) FROM item_slots WHERE id=9001"));
    }

    [Fact]
    public async Task Offline_delivery_uses_persistent_container_without_registering_an_online_bag()
    {
        using var f = Fixture();
        var service = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        service.Clear(new(1));
        await service.AddAsync(new(1), 9001, 3, default);
        Assert.Null(service.FindContainer(new(1), InventoryContainerType.Main));
        Assert.Equal(3, f.Count(9001));
    }

    [SkippableFact]
    public async Task Offline_creature_delivery_creates_details_without_online_registration()
    {
        Skip.If(new Dfo.Server.Infrastructure.Content.PvfContentOptions().ResolveArchivePath() is null,
            "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var containers = shop.Host.Services.GetRequiredService<ICharacterContainerDomainService>();
        containers.Clear(shop.Host.Session.CharacterId);

        var added = await containers.AddAsync(shop.Host.Session.CharacterId, 63000, 1, default);

        var item = Assert.Single(added.Items);
        Assert.Equal(BusinessInventorySpace.CreatureInventory, item.Slot.Space);
        Assert.Equal(0, item.Slot.Index);
        Assert.Null(containers.FindContainer(shop.Host.Session.CharacterId, InventoryContainerType.Pet));
        Assert.Equal(1, shop.Host.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=7 AND slot_index=0"));
        Assert.Equal(1, shop.Host.Scalar("SELECT COUNT(*) FROM item_slots s JOIN items i ON i.item_uid=s.core_uid WHERE s.owner_scope='character' AND s.owner_id=1 AND s.list_type=7 AND s.slot_index=0 AND i.creature_field04=100"));
    }

    private static ShopFixture Fixture() => new(s => s.AddSingleton<IItemPlacementDefinitionSource>(new Content()));
    private sealed class Content : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<ItemPlacement?>(id switch
        {
            3037 => new(11, 1000, new(BusinessInventorySpace.Material, 0, 55)),
            9001 => new(3, 2, new(BusinessInventorySpace.Material, 0, 2)),
            9002 => new(7, 10, new(BusinessInventorySpace.CreatureConsumable, 0, 1)),
            _ => null
        });
    }
}
