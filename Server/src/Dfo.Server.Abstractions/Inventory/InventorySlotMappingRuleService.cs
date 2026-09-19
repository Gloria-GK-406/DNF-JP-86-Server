using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>The one table that gives A21 game slots their business meaning.</summary>
public sealed class InventorySlotMappingRuleService : IInventorySlotMappingRuleService
{
    public BusinessSlot ToBusinessSlot(InventoryStorageSlot source)
    {
        ArgumentNullException.ThrowIfNull(source.Container);
        return this.ToBusinessSlot(new GameSlot(source.Container.Type, source.Index));
    }

    public BusinessSlot ToBusinessSlot(GameSlot source) => source.Space switch
    {
        InventoryContainerType.Main when In(source.Index, 3, 8) => Business(BusinessInventorySpace.QuickSlot, source.Index, 3),
        InventoryContainerType.Main when In(source.Index, 9, 64) => Business(BusinessInventorySpace.CarriedEquipment, source.Index, 9),
        InventoryContainerType.Main when In(source.Index, 65, 120) => Business(BusinessInventorySpace.Consumable, source.Index, 65),
        InventoryContainerType.Main when In(source.Index, 121, 176) => Business(BusinessInventorySpace.Material, source.Index, 121),
        InventoryContainerType.Main when In(source.Index, 177, 232) => Business(BusinessInventorySpace.Quest, source.Index, 177),
        InventoryContainerType.Main when In(source.Index, 233, 288) => Business(BusinessInventorySpace.ExpertMaterial, source.Index, 233),
        InventoryContainerType.Main when In(source.Index, 289, 351) => Business(BusinessInventorySpace.AvatarEmblem, source.Index, 289),
        InventoryContainerType.Avatar when In(source.Index, 0, 209) => Business(BusinessInventorySpace.Avatar, source.Index, 0),
        InventoryContainerType.PersonalCargo when In(source.Index, 0, 151) => Business(BusinessInventorySpace.PersonalCargo, source.Index, 0),
        InventoryContainerType.Equipment when In(source.Index, 0, 10) => Business(BusinessInventorySpace.WornAvatar, source.Index, 0),
        InventoryContainerType.Equipment when In(source.Index, 11, 23) => Business(BusinessInventorySpace.WornEquipment, source.Index, 11),
        InventoryContainerType.Equipment when source.Index == 24 => new(BusinessInventorySpace.WornCreature, 0),
        InventoryContainerType.Equipment when In(source.Index, 25, 27) => Business(BusinessInventorySpace.WornCreatureEquipment, source.Index, 25),
        InventoryContainerType.Equipment when source.Index == 29 => new(BusinessInventorySpace.WornEquipmentExtension, 0),
        InventoryContainerType.Pet when In(source.Index, 0, 139) => Business(BusinessInventorySpace.CreatureInventory, source.Index, 0),
        InventoryContainerType.Pet when In(source.Index, 140, 188) => Business(BusinessInventorySpace.CreatureEquipment, source.Index, 140),
        InventoryContainerType.Pet when In(source.Index, 189, 239) => Business(BusinessInventorySpace.CreatureConsumable, source.Index, 189),
        InventoryContainerType.AccountCargo when In(source.Index, 0, 63) => Business(BusinessInventorySpace.AccountCargo, source.Index, 0),
        InventoryContainerType.GuildMedal when In(source.Index, 0, 48) => Business(BusinessInventorySpace.GuildMedal, source.Index, 0),
        InventoryContainerType.GuildMedal when In(source.Index, 49, 97) => Business(BusinessInventorySpace.GuardianGem, source.Index, 49),
        _ => throw new InventorySlotMappingException(source),
    };

    public GameSlot ToGameSlot(BusinessSlot source) => source.Space switch
    {
        BusinessInventorySpace.QuickSlot => Game(source, InventoryContainerType.Main, 3, 5),
        BusinessInventorySpace.CarriedEquipment => Game(source, InventoryContainerType.Main, 9, 55),
        BusinessInventorySpace.Consumable => Game(source, InventoryContainerType.Main, 65, 55),
        BusinessInventorySpace.Material => Game(source, InventoryContainerType.Main, 121, 55),
        BusinessInventorySpace.Quest => Game(source, InventoryContainerType.Main, 177, 55),
        BusinessInventorySpace.ExpertMaterial => Game(source, InventoryContainerType.Main, 233, 55),
        BusinessInventorySpace.AvatarEmblem => Game(source, InventoryContainerType.Main, 289, 62),
        BusinessInventorySpace.Avatar => Game(source, InventoryContainerType.Avatar, 0, 209),
        BusinessInventorySpace.PersonalCargo => Game(source, InventoryContainerType.PersonalCargo, 0, 151),
        BusinessInventorySpace.WornAvatar => Game(source, InventoryContainerType.Equipment, 0, 10),
        BusinessInventorySpace.WornEquipment => Game(source, InventoryContainerType.Equipment, 11, 12),
        BusinessInventorySpace.WornCreature => Game(source, InventoryContainerType.Equipment, 24, 0),
        BusinessInventorySpace.WornCreatureEquipment => Game(source, InventoryContainerType.Equipment, 25, 2),
        BusinessInventorySpace.WornEquipmentExtension => Game(source, InventoryContainerType.Equipment, 29, 0),
        BusinessInventorySpace.CreatureInventory => Game(source, InventoryContainerType.Pet, 0, 139),
        BusinessInventorySpace.CreatureEquipment => Game(source, InventoryContainerType.Pet, 140, 48),
        BusinessInventorySpace.CreatureConsumable => Game(source, InventoryContainerType.Pet, 189, 50),
        BusinessInventorySpace.AccountCargo => Game(source, InventoryContainerType.AccountCargo, 0, 63),
        BusinessInventorySpace.GuildMedal => Game(source, InventoryContainerType.GuildMedal, 0, 48),
        BusinessInventorySpace.GuardianGem => Game(source, InventoryContainerType.GuildMedal, 49, 48),
        _ => throw new InventorySlotMappingException(source),
    };

    private static bool In(int value, int start, int end) => value >= start && value <= end;

    private static BusinessSlot Business(BusinessInventorySpace space, int index, int offset) =>
        new(space, checked(index - offset));

    private static GameSlot Game(BusinessSlot source, InventoryContainerType space, int offset, int maxIndex)
    {
        if (!In(source.Index, 0, maxIndex)) throw new InventorySlotMappingException(source);
        return new(space, checked(source.Index + offset));
    }
}
