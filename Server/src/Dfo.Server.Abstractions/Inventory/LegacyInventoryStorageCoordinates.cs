using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Adapts local business slots to the existing item_slots list and index columns.</summary>
public readonly record struct LegacyInventoryStorageCoordinate(InventoryContainerType List, int Index);

public static class LegacyInventoryStorageCoordinates
{
    private readonly record struct Band(BusinessInventorySpace Space, InventoryContainerType List,
        int First, int Last);

    private static readonly Band[] Bands =
    [
        new(BusinessInventorySpace.TowerQuickSlot, InventoryContainerType.QuickSlot, 3, 8),
        new(BusinessInventorySpace.QuickSlot, InventoryContainerType.Main, 3, 8),
        new(BusinessInventorySpace.CarriedEquipment, InventoryContainerType.Main, 9, 64),
        new(BusinessInventorySpace.Consumable, InventoryContainerType.Main, 65, 120),
        new(BusinessInventorySpace.Material, InventoryContainerType.Main, 121, 176),
        new(BusinessInventorySpace.Quest, InventoryContainerType.Main, 177, 232),
        new(BusinessInventorySpace.ExpertMaterial, InventoryContainerType.Main, 233, 288),
        new(BusinessInventorySpace.AvatarEmblem, InventoryContainerType.Main, 289, 351),
        new(BusinessInventorySpace.Avatar, InventoryContainerType.Avatar, 0, 209),
        new(BusinessInventorySpace.PersonalCargo, InventoryContainerType.PersonalCargo, 0, 199),
        new(BusinessInventorySpace.WornAvatar, InventoryContainerType.Equipment, 0, 11),
        new(BusinessInventorySpace.WornEquipment, InventoryContainerType.Equipment, 12, 24),
        new(BusinessInventorySpace.WornCreature, InventoryContainerType.Equipment, 25, 25),
        new(BusinessInventorySpace.WornCreatureEquipment, InventoryContainerType.Equipment, 26, 28),
        new(BusinessInventorySpace.WornEquipmentExtension, InventoryContainerType.Equipment, 29, 31),
        new(BusinessInventorySpace.CreatureInventory, InventoryContainerType.Pet, 0, 139),
        new(BusinessInventorySpace.CreatureEquipment, InventoryContainerType.Pet, 140, 188),
        new(BusinessInventorySpace.CreatureConsumable, InventoryContainerType.Pet, 189, 239),
        new(BusinessInventorySpace.AccountCargo, InventoryContainerType.AccountCargo, 0, 119),
        new(BusinessInventorySpace.SharedMaterial, InventoryContainerType.SharedMaterial, 0, 10),
        new(BusinessInventorySpace.GuildMedal, InventoryContainerType.GuildMedal, 0, 48),
        new(BusinessInventorySpace.GuardianGem, InventoryContainerType.GuildMedal, 49, 97),
    ];

    public static LegacyInventoryStorageCoordinate ToStorage(BusinessSlot slot)
    {
        // The legacy 24/25..27 creature positions are normalized before inventory entry.
        // Ordinary worn equipment retains its actual physical 12..24 coordinates.
        foreach (var band in Bands)
            if (band.Space == slot.Space && slot.Index >= 0 && slot.Index <= band.Last - band.First)
                return new(band.List, checked(band.First + slot.Index));
        throw new InventorySlotMappingException(slot);
    }

    public static BusinessSlot ToBusiness(LegacyInventoryStorageCoordinate coordinate)
    {
        if (coordinate is { List: InventoryContainerType.Equipment, Index: 24 })
            throw new InventorySlotMappingException(new GameSlot(coordinate.List, coordinate.Index));
        foreach (var band in Bands)
            if (band.List == coordinate.List && coordinate.Index >= band.First && coordinate.Index <= band.Last)
                return new(band.Space, coordinate.Index - band.First);
        throw new InventorySlotMappingException(new GameSlot(coordinate.List, coordinate.Index));
    }

    public static BusinessSlot ToBusiness(LegacyInventoryStorageCoordinate coordinate, byte itemKind)
    {
        if (coordinate is { List: InventoryContainerType.Equipment, Index: 24 })
            return itemKind switch
            {
                1 => new(BusinessInventorySpace.WornEquipment, 12),
                5 => new(BusinessInventorySpace.WornCreature, 0),
                _ => throw new InvalidDataException($"Legacy equipment slot 24 has unsupported kind {itemKind}."),
            };
        if (coordinate is { List: InventoryContainerType.Equipment, Index: 25 } && itemKind == 6)
            throw new InvalidDataException("Legacy artifact at equipment/25 must be normalized before use.");
        return ToBusiness(coordinate);
    }
}
