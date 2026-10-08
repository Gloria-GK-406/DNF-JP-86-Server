using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Abstractions.Inventory;
public interface IEquipmentWearDomainService
{
    /// <summary>Unseal an item for a successful equip; callers persist it in the move transaction.</summary>
    ItemSnapshot UnsealForEquip(ItemSnapshot item);
    ValueTask<bool> CanWearAsync(CharacterId characterId, int itemId, CancellationToken cancellationToken);
}
public interface IEquipmentWearRuleService
{
    ItemSnapshot UnsealForEquip(ItemSnapshot item);
    ValueTask<int?> MinimumLevelAsync(int itemId, CancellationToken cancellationToken);
}
