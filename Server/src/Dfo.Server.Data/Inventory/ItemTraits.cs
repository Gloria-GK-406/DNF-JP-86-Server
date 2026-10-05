namespace Dfo.Server.Data.Inventory;

/// <summary>Coarse template features used for container admission, never current ownership.</summary>
[Flags]
public enum ItemTraits
{
    None = 0,
    Material = 1 << 0,
    Equipment = 1 << 1,
    Consumable = 1 << 2,
    AccountShared = 1 << 3,
    Quest = 1 << 4,
    Profession = 1 << 5,
    Avatar = 1 << 6,
    Creature = 1 << 7,
    Tower = 1 << 8,
}

public static class ItemTraitClassification
{
    /// <summary>Kind must come from the template for authoritative admission.</summary>
    public static ItemTraits For(byte itemKind, int itemId = 0)
    {
        var traits = itemKind switch
        {
            1 => ItemTraits.Equipment,
            2 => ItemTraits.Consumable,
            3 or 11 => ItemTraits.Material,
            4 => ItemTraits.Quest,
            5 => ItemTraits.Creature,
            6 => ItemTraits.Equipment | ItemTraits.Creature,
            7 => ItemTraits.Consumable | ItemTraits.Creature,
            8 => ItemTraits.Avatar,
            9 => ItemTraits.Material | ItemTraits.Avatar,
            10 => ItemTraits.Material | ItemTraits.Profession,
            _ => ItemTraits.None,
        };
        // A21 stackable.lst tower consumables 6516..6525 and clear cube 6515.
        if (itemId is >= 6515 and <= 6525 && traits is ItemTraits.Consumable or ItemTraits.Material)
            traits |= ItemTraits.Tower;
        return traits != ItemTraits.None && AccountMaterialCatalog.ContainsItem(itemId)
            ? traits | ItemTraits.AccountShared : traits;
    }
}

/// <summary>Container feature rules. Slot and instance rules apply separately.</summary>
public static class ItemContainerAdmission
{
    private const ItemTraits Cargo = ItemTraits.Equipment | ItemTraits.Consumable
        | ItemTraits.Material | ItemTraits.Profession | ItemTraits.AccountShared;

    public static ItemAdmissionRule For(BusinessInventorySpace space) => space == BusinessInventorySpace.TowerQuickSlot
        ? new(ItemTraits.Tower | ItemTraits.Consumable | ItemTraits.Material,
            all: ItemTraits.Tower, any: ItemTraits.Consumable | ItemTraits.Material)
        : new(space switch
    {
        BusinessInventorySpace.QuickSlot => Cargo | ItemTraits.Quest,
        BusinessInventorySpace.CarriedEquipment or BusinessInventorySpace.WornEquipment
            or BusinessInventorySpace.WornEquipmentExtension => ItemTraits.Equipment,
        BusinessInventorySpace.Consumable => ItemTraits.Consumable,
        BusinessInventorySpace.Material or BusinessInventorySpace.SharedMaterial =>
            ItemTraits.Material | ItemTraits.AccountShared,
        BusinessInventorySpace.Quest => ItemTraits.Quest,
        BusinessInventorySpace.ExpertMaterial => ItemTraits.Material | ItemTraits.Profession,
        BusinessInventorySpace.AvatarEmblem => ItemTraits.Material | ItemTraits.Avatar,
        BusinessInventorySpace.Avatar or BusinessInventorySpace.WornAvatar => ItemTraits.Avatar,
        BusinessInventorySpace.CreatureInventory or BusinessInventorySpace.WornCreature => ItemTraits.Creature,
        BusinessInventorySpace.CreatureEquipment or BusinessInventorySpace.WornCreatureEquipment =>
            ItemTraits.Equipment | ItemTraits.Creature,
        BusinessInventorySpace.CreatureConsumable => ItemTraits.Consumable | ItemTraits.Creature,
        BusinessInventorySpace.PersonalCargo or BusinessInventorySpace.AccountCargo => Cargo,
        // Virtual resources and the not-yet-implemented medal spaces have no item admission.
        _ => ItemTraits.None,
    });
}
