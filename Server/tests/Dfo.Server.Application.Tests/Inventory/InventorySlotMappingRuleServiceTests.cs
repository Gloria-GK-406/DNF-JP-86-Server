using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class InventorySlotMappingRuleServiceTests
{
    private readonly InventorySlotMappingRuleService _rules = new();

    [Theory]
    [InlineData(24, BusinessInventorySpace.WornEquipment, 12)]
    [InlineData(25, BusinessInventorySpace.WornCreature, 0)]
    [InlineData(26, BusinessInventorySpace.WornCreatureEquipment, 0)]
    [InlineData(28, BusinessInventorySpace.WornCreatureEquipment, 2)]
    public void Upstream_a21_worn_slot_candidates_are_independent_of_legacy_storage(
        int wireIndex, BusinessInventorySpace expectedSpace, int localIndex)
    {
        var business = new BusinessSlot(expectedSpace, localIndex);
        Assert.Equal(business, _rules.ToBusinessSlot(new GameSlot(InventoryContainerType.Equipment, wireIndex)));
        Assert.Equal(new GameSlot(InventoryContainerType.Equipment, wireIndex), _rules.ToGameSlot(business));
    }

    [Fact]
    public void Legacy_worn_slot_24_requires_item_identity_to_distinguish_earring_and_creature()
    {
        var coordinate = new LegacyInventoryStorageCoordinate(InventoryContainerType.Equipment, 24);
        Assert.Throws<InventorySlotMappingException>(() => LegacyInventoryStorageCoordinates.ToBusiness(coordinate));
        Assert.Equal(new(BusinessInventorySpace.WornEquipment, 12),
            LegacyInventoryStorageCoordinates.ToBusiness(coordinate, 1));
        Assert.Equal(new(BusinessInventorySpace.WornCreature, 0),
            LegacyInventoryStorageCoordinates.ToBusiness(coordinate, 5));
        Assert.Throws<InvalidDataException>(() => LegacyInventoryStorageCoordinates.ToBusiness(coordinate, 8));
    }

    [Fact]
    public void Existing_weapon_at_physical_12_remains_weapon_at_wire_12()
    {
        var physical = new LegacyInventoryStorageCoordinate(InventoryContainerType.Equipment, 12);
        var business = LegacyInventoryStorageCoordinates.ToBusiness(physical, 1);
        Assert.Equal(new(BusinessInventorySpace.WornEquipment, 0), business);
        Assert.Equal(physical, LegacyInventoryStorageCoordinates.ToStorage(business));
        Assert.Equal(new GameSlot(InventoryContainerType.Equipment, 12), _rules.ToGameSlot(business));
    }

    public static TheoryData<GameSlot, BusinessSlot> Boundaries => new()
    {
        { new(InventoryContainerType.Main, 3), new(BusinessInventorySpace.QuickSlot, 0) },
        { new(InventoryContainerType.Main, 8), new(BusinessInventorySpace.QuickSlot, 5) },
        { new(InventoryContainerType.Main, 9), new(BusinessInventorySpace.CarriedEquipment, 0) },
        { new(InventoryContainerType.Main, 64), new(BusinessInventorySpace.CarriedEquipment, 55) },
        { new(InventoryContainerType.Main, 65), new(BusinessInventorySpace.Consumable, 0) },
        { new(InventoryContainerType.Main, 120), new(BusinessInventorySpace.Consumable, 55) },
        { new(InventoryContainerType.Main, 121), new(BusinessInventorySpace.Material, 0) },
        { new(InventoryContainerType.Main, 176), new(BusinessInventorySpace.Material, 55) },
        { new(InventoryContainerType.Main, 177), new(BusinessInventorySpace.Quest, 0) },
        { new(InventoryContainerType.Main, 232), new(BusinessInventorySpace.Quest, 55) },
        { new(InventoryContainerType.Main, 233), new(BusinessInventorySpace.ExpertMaterial, 0) },
        { new(InventoryContainerType.Main, 288), new(BusinessInventorySpace.ExpertMaterial, 55) },
        { new(InventoryContainerType.Main, 289), new(BusinessInventorySpace.AvatarEmblem, 0) },
        { new(InventoryContainerType.Main, 351), new(BusinessInventorySpace.AvatarEmblem, 62) },
        { new(InventoryContainerType.Avatar, 0), new(BusinessInventorySpace.Avatar, 0) },
        { new(InventoryContainerType.Avatar, 209), new(BusinessInventorySpace.Avatar, 209) },
        { new(InventoryContainerType.PersonalCargo, 0), new(BusinessInventorySpace.PersonalCargo, 0) },
        { new(InventoryContainerType.PersonalCargo, 151), new(BusinessInventorySpace.PersonalCargo, 151) },
        { new(InventoryContainerType.Equipment, 0), new(BusinessInventorySpace.WornAvatar, 0) },
        { new(InventoryContainerType.Equipment, 11), new(BusinessInventorySpace.WornAvatar, 11) },
        { new(InventoryContainerType.Equipment, 12), new(BusinessInventorySpace.WornEquipment, 0) },
        { new(InventoryContainerType.Equipment, 24), new(BusinessInventorySpace.WornEquipment, 12) },
        { new(InventoryContainerType.Equipment, 25), new(BusinessInventorySpace.WornCreature, 0) },
        { new(InventoryContainerType.Equipment, 26), new(BusinessInventorySpace.WornCreatureEquipment, 0) },
        { new(InventoryContainerType.Equipment, 28), new(BusinessInventorySpace.WornCreatureEquipment, 2) },
        { new(InventoryContainerType.Equipment, 29), new(BusinessInventorySpace.WornEquipmentExtension, 0) },
        { new(InventoryContainerType.Pet, 0), new(BusinessInventorySpace.CreatureInventory, 0) },
        { new(InventoryContainerType.Pet, 139), new(BusinessInventorySpace.CreatureInventory, 139) },
        { new(InventoryContainerType.Pet, 140), new(BusinessInventorySpace.CreatureEquipment, 0) },
        { new(InventoryContainerType.Pet, 188), new(BusinessInventorySpace.CreatureEquipment, 48) },
        { new(InventoryContainerType.Pet, 189), new(BusinessInventorySpace.CreatureConsumable, 0) },
        { new(InventoryContainerType.Pet, 239), new(BusinessInventorySpace.CreatureConsumable, 50) },
        { new(InventoryContainerType.AccountCargo, 0), new(BusinessInventorySpace.AccountCargo, 0) },
        { new(InventoryContainerType.AccountCargo, 63), new(BusinessInventorySpace.AccountCargo, 63) },
        { new(InventoryContainerType.GuildMedal, 0), new(BusinessInventorySpace.GuildMedal, 0) },
        { new(InventoryContainerType.GuildMedal, 48), new(BusinessInventorySpace.GuildMedal, 48) },
        { new(InventoryContainerType.GuildMedal, 49), new(BusinessInventorySpace.GuardianGem, 0) },
        { new(InventoryContainerType.GuildMedal, 97), new(BusinessInventorySpace.GuardianGem, 48) },
    };

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Every_supported_boundary_round_trips(GameSlot game, BusinessSlot business)
    {
        Assert.Equal(business, _rules.ToBusinessSlot(game));
        Assert.Equal(game, _rules.ToGameSlot(business));
    }

    public static TheoryData<GameSlot> InvalidGameSlots => new()
    {
        new(InventoryContainerType.Main, 0),
        new(InventoryContainerType.Main, 2),
        new(InventoryContainerType.Main, 352),
        new(InventoryContainerType.Main, 359),
        new(InventoryContainerType.Equipment, 32),
        new(InventoryContainerType.QuickSlot, 0),
        new((InventoryContainerType)255, 0),
        new(InventoryContainerType.Main, -1),
    };

    [Theory]
    [MemberData(nameof(InvalidGameSlots))]
    public void Invalid_game_slots_throw_with_the_source(GameSlot source)
    {
        var failure = Assert.Throws<InventorySlotMappingException>(() => _rules.ToBusinessSlot(source));

        Assert.Equal(source, failure.GameSlot);
        Assert.Null(failure.BusinessSlot);
    }

    public static TheoryData<BusinessSlot> InvalidBusinessSlots => new()
    {
        new(BusinessInventorySpace.QuickSlot, 6),
        new(BusinessInventorySpace.WornEquipment, 13),
        new(BusinessInventorySpace.Avatar, 210),
        new(BusinessInventorySpace.Consumable, -1),
        new(BusinessInventorySpace.VirtualResource, 0),
        new((BusinessInventorySpace)999, 0),
    };

    [Theory]
    [MemberData(nameof(InvalidBusinessSlots))]
    public void Invalid_business_slots_throw_with_the_source(BusinessSlot source)
    {

        var failure = Assert.Throws<InventorySlotMappingException>(() => _rules.ToGameSlot(source));

        Assert.Equal(source, failure.BusinessSlot);
        Assert.Null(failure.GameSlot);
    }
}
