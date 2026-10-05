using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemCatalogRuleService(IItemCatalogSource source, IPackageOpeningDefinitionSource definitions,
    IItemPlacementRuleService placements, IItemEquipmentRuleService equipment) : IItemCatalogRuleService
{
    public ValueTask<IReadOnlyList<ItemCatalogEntry>> CandidatesAsync(ItemCatalogFilter filter, CancellationToken ct)
    {
        if (filter.Page is < 1 or > 10000 || filter.PageSize is < 1 or > 50 || filter.ItemId <= 0
            || filter.Q?.Length > 128 || filter.EquipmentType?.Length > 64
            || filter.Category is not (null or "equipment" or "material" or "consumable" or "quest" or "other")
            || filter.MinimumLevel < 0 || filter.MaximumLevel < 0 || filter.MinimumLevel > filter.MaximumLevel
            || filter.Rarity is < 0 or > 6) throw new ArgumentException("物品查询参数无效。");
        return source.SearchAsync(filter, ct);
    }

    public async ValueTask<ItemCatalogDefinition> DefinitionAsync(int itemId, CancellationToken ct)
    {
        if (itemId <= 0) throw new ArgumentException("物品 ID 必须为正整数。");
        var entry = await source.FindAsync(itemId, ct) ?? throw new KeyNotFoundException("未找到该物品。");
        var item = await definitions.GetRewardAsync(itemId, ct) ?? throw new KeyNotFoundException("物品模板不存在。");
        var placement = await placements.PlacementForAsync(itemId, ct) ?? throw new ArgumentException("物品没有可投递的位置。");
        return new(entry, item, placement);
    }

    public async ValueTask<ItemCatalogDetails> DetailsAsync(ItemCatalogDefinition definition, CancellationToken ct)
    {
        var item = definition.Item; var p = definition.Placement;
        var core = new ItemSnapshot { ItemId = item.ItemId, Count = 1, IsPile = p.WorldClass != ItemWorldClass.Equipment,
            Type = p.Traits, LegacyKind = p.ItemKind, Marker16 = -1, RandomOptionChangedIndex = byte.MaxValue,
            Durability = item.Durability, SealFlag = item.Sealed ? (byte)1 : (byte)0 };
        var kind = p.WorldClass switch
        {
            ItemWorldClass.Equipment => "equipment",
            ItemWorldClass.Avatar => "avatar",
            ItemWorldClass.Creature => "creature",
            _ => "stackable"
        };
        return new(definition.Entry, kind, item.Sealed, item.Durability,
            p.WorldClass == ItemWorldClass.Equipment ? await equipment.CapabilitiesAsync(core, ct) : null);
    }

    public async ValueTask<string?> ItemNameAsync(int itemId, CancellationToken ct) => (await source.FindAsync(itemId, ct))?.Name;
}

internal static class PermanentOrdinaryItemRules
{
    public static bool Allows(ItemCatalogDefinition definition)
    {
        var item = definition.Item; var p = definition.Placement;
        return item.ItemId == definition.Entry.ItemId && item.Supported && item.DefaultPeriodDays == 0
            && item.AbsoluteExpiration == 0 && item.PremiumType == 0 && p.ItemKind is not (0 or 5 or 6 or 7 or 8)
            && item.Placement.ItemKind == p.ItemKind && item.Placement.WorldClass == p.WorldClass
            && item.Placement.Destination.Space != BusinessInventorySpace.VirtualResource
            && p.Destination.Space != BusinessInventorySpace.VirtualResource
            && p.WorldClass is ItemWorldClass.Equipment or ItemWorldClass.Interchangeable;
    }

    public static bool IsOrdinaryBackpack(BusinessInventorySpace space) => space is BusinessInventorySpace.CarriedEquipment
        or BusinessInventorySpace.Consumable or BusinessInventorySpace.Material or BusinessInventorySpace.Quest
        or BusinessInventorySpace.ExpertMaterial or BusinessInventorySpace.AvatarEmblem;
}
