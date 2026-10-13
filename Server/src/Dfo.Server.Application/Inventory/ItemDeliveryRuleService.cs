using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal interface IItemDeliveryRuleService
{
    ValueTask<ItemSnapshot> TemplateAsync(int itemId, CancellationToken ct);
}
internal sealed class ItemDeliveryRuleService(IItemPlacementRuleService placements, ICreatureDefinitionSource creatures,
    IEquipmentDefinitionSource equipment) : IItemDeliveryRuleService
{
    public async ValueTask<ItemSnapshot> TemplateAsync(int itemId, CancellationToken ct)
    {
        var p = await placements.PlacementForAsync(itemId, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Unknown reward {itemId}.");
        var value = new ItemSnapshot { ItemId = itemId, LegacyKind = p.ItemKind, Type = p.Traits,
            IsPile = p.WorldClass == ItemWorldClass.Interchangeable, Count = 1 };
        if (p.WorldClass == ItemWorldClass.Equipment)
        {
            var definition = await equipment.GetEquipmentAsync(itemId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Unknown equipment reward {itemId}.");
            value = value with { Durability = definition.MaxDurability ?? 0 };
        }
        if (p.WorldClass == ItemWorldClass.Avatar) value = value with { AvatarDetail = ArrivingAvatar.Facts() };
        if (p.WorldClass == ItemWorldClass.Creature)
        {
            var creature = await creatures.FindByEquipmentAsync(itemId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Unknown creature reward {itemId}.");
            value = value with { CreatureDetail = new(100, 0, 0, 0, 0, creature.StartLevel, [], 0) };
        }
        return value;
    }
}
