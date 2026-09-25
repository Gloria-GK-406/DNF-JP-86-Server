using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ContainerAdditionTests
{
    [Fact]
    public async Task Routes_material_and_pet_consumable_without_a_caller_selected_bag()
    {
        using var f = Fixture();
        var service = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        var material = await service.AddAsync(new(1), 9001, 5, default);
        Assert.Equal(new[] { 121, 122, 123 }, material.Items.Select(x => x.SlotIndex));
        Assert.Equal(new[] { 2, 2, 1 }, material.Items.Select(x => x.Count));
        Assert.All(material.Items, x => Assert.Equal(InventoryContainerType.Main, x.Type));
        var pet = await service.AddAsync(new(1), 9002, 1, default);
        Assert.Equal(InventoryContainerType.Pet, Assert.Single(pet.Items).Type);
        Assert.Equal(189, pet.Items[0].SlotIndex);
        Assert.Equal(5, f.Count(9001));
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
        Assert.Empty(addition.Items);
        Assert.Equal(3037, addition.AccountMaterial!.ItemId);
        Assert.Equal(3, addition.AccountMaterial.AddedCount);
        Assert.Equal(0, f.Count(3037));
        Assert.Equal(3, f.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
    }

    [Fact]
    public async Task Misregistered_persistent_container_cannot_write_another_characters_bag()
    {
        using var f = Fixture();
        var service = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        var wrong = new PersistentItemContainer(ContainerId.ForCharacter(new(2), InventoryContainerType.Main),
            new(2), new(InventoryContainerType.Main, 0), f.Services.GetRequiredService<IItemDomainStore>(),
            f.Services.GetRequiredService<IDomainTransactionAccessor>(), null,
            f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemArrivalRuleService>(),
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

    private static ShopFixture Fixture() => new(s => s.AddSingleton<IItemPlacementDefinitionSource>(new Content()));
    private sealed class Content : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<ItemPlacement?>(id switch
        {
            9001 => new(3, 2, new(BusinessInventorySpace.Material, 0, 2)),
            9002 => new(7, 10, new(BusinessInventorySpace.CreatureConsumable, 0, 1)),
            _ => null
        });
    }
}
