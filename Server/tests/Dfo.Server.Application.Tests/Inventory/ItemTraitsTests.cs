using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ItemTraitsTests
{
    [Theory]
    [InlineData(1, ItemTraits.Equipment)]
    [InlineData(2, ItemTraits.Consumable)]
    [InlineData(3, ItemTraits.Material)]
    [InlineData(4, ItemTraits.Quest)]
    [InlineData(5, ItemTraits.Creature)]
    [InlineData(6, ItemTraits.Equipment | ItemTraits.Creature)]
    [InlineData(7, ItemTraits.Consumable | ItemTraits.Creature)]
    [InlineData(8, ItemTraits.Avatar)]
    [InlineData(9, ItemTraits.Material | ItemTraits.Avatar)]
    [InlineData(10, ItemTraits.Material | ItemTraits.Profession)]
    [InlineData(11, ItemTraits.Material)]
    public void Core_projects_the_coarse_template_features(byte kind, ItemTraits expected) =>
        Assert.Equal(expected, StackableItemCore.Create(kind, 999999, 1).Traits);

    [Fact]
    public void Accepting_a_feature_allows_but_does_not_require_it()
    {
        var material = ItemTraits.Material;
        var shared = material | ItemTraits.AccountShared;
        Assert.True(new ItemAdmissionRule(shared).Matches(material));
        Assert.True(new ItemAdmissionRule(shared).Matches(shared));
        Assert.False(new ItemAdmissionRule(material).Matches(shared));
        Assert.False(new ItemAdmissionRule(shared).Matches(ItemTraits.None));
        Assert.False(new ItemAdmissionRule(shared).Matches(material | (ItemTraits)256));
    }

    [Theory]
    [InlineData(BusinessInventorySpace.CarriedEquipment, 1)]
    [InlineData(BusinessInventorySpace.Consumable, 2)]
    [InlineData(BusinessInventorySpace.Material, 3)]
    [InlineData(BusinessInventorySpace.Quest, 4)]
    [InlineData(BusinessInventorySpace.CreatureInventory, 5)]
    [InlineData(BusinessInventorySpace.CreatureEquipment, 6)]
    [InlineData(BusinessInventorySpace.CreatureConsumable, 7)]
    [InlineData(BusinessInventorySpace.Avatar, 8)]
    [InlineData(BusinessInventorySpace.AvatarEmblem, 9)]
    [InlineData(BusinessInventorySpace.ExpertMaterial, 10)]
    public void A_container_accepts_its_category_but_never_unknown_bits(BusinessInventorySpace space, byte kind)
    {
        var rule = ItemContainerAdmission.For(space);
        var traits = ItemTraitClassification.For(kind);
        Assert.True(rule.Matches(traits));
        Assert.False(rule.Matches(traits | (ItemTraits)256));
    }

    [Theory]
    [InlineData(BusinessInventorySpace.PersonalCargo)]
    [InlineData(BusinessInventorySpace.AccountCargo)]
    public void Cargo_keeps_the_existing_category_boundary(BusinessInventorySpace space)
    {
        var rule = ItemContainerAdmission.For(space);
        for (byte kind = 0; kind < 12; kind++)
            Assert.Equal(kind is 1 or 2 or 3 or 10 or 11,
                rule.Matches(ItemTraitClassification.For(kind)));
        foreach (var definition in AccountMaterialCatalog.Definitions)
            Assert.True(rule.Matches(StackableItemCore.Create(definition.ItemKind, definition.ItemId, 1).Traits));
    }

    [Fact]
    public async Task A_direct_business_write_cannot_mislabel_equipment_as_a_consumable()
    {
        using var host = WarehouseStorageTests.Fixture();
        var bag = WarehouseStorageTests.Bag(host, BusinessInventorySpace.Consumable);
        Assert.Equal(new ItemAdmissionRule(ItemTraits.Consumable), bag.Admission);
        await Assert.ThrowsAsync<InvalidDataException>(() => WarehouseStorageTests.InTransaction(host,
            async () => await bag.PutAsync(0, await host.Services.GetRequiredService<IItemLifecycleDomainService>().CreateAsync(
                ItemSnapshot.FromCore(StackableItemCore.Create(2, 3000, 1), true), default), default)));
        Assert.Null(await bag.GetAsync(0, default));
    }
}
