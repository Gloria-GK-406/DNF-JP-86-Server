using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Abstractions.Inventory;
public interface IEquipmentWearDomainService
{
    /// <summary>Unseal an item for a successful equip; callers persist it in the move transaction.</summary>
    ItemSnapshot UnsealForEquip(ItemSnapshot item);
    /// <summary>Prepare the new worn value inside the movement transaction.</summary>
    ValueTask<ItemSnapshot> PrepareForWearAsync(ItemSnapshot item, ItemSnapshot? previous, CancellationToken ct) =>
        ValueTask.FromResult(UnsealForEquip(item));
    ValueTask<bool> CanWearAsync(CharacterId characterId, int itemId, CancellationToken cancellationToken);
}
public interface IEquipmentWearRuleService
{
    ItemSnapshot UnsealForEquip(ItemSnapshot item);
    ValueTask<int?> MinimumLevelAsync(int itemId, CancellationToken cancellationToken);
}
