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

    /// <summary>
    /// Whether this character's career carries this weapon as a second weapon in that slot
    /// (流浪武士 and a beam sword, in the current archive).
    /// </summary>
    /// <remarks>Only the second-weapon allowance; the slot an item names for itself is asked of placement.</remarks>
    ValueTask<bool> MayWearAsSupportWeaponAsync(CharacterId characterId, int itemId, BusinessSlot slot,
        CancellationToken cancellationToken) => ValueTask.FromResult(false);
}
public interface IEquipmentWearRuleService
{
    ItemSnapshot UnsealForEquip(ItemSnapshot item);
    ValueTask<int?> MinimumLevelAsync(int itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a career's <c>[support weapon sub type]</c> allowance admits this weapon in that slot.
    /// </summary>
    ValueTask<bool> MayCarryAsSupportWeaponAsync(CharacterCareer career, int itemId, BusinessSlot slot,
        CancellationToken cancellationToken);
}
