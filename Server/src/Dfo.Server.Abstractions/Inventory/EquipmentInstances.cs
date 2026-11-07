using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEquipmentDomainService
{
    IEquipmentCollection Of(CharacterId owner);
}

public interface IEquipmentCollection
{
    ValueTask<IEquipmentInstance?> FindAsync(int itemUid, CancellationToken cancellationToken);
    ValueTask<IEquipmentInstance?> FindAtAsync(BusinessSlot slot, CancellationToken cancellationToken);
}

/// <summary>A transaction-bound identity. Rules decide the values these operations apply.</summary>
public interface IEquipmentInstance : IItemSocketInstance
{
    ItemReference Reference { get; }
    int ItemUid { get; }
    ValueTask<ItemReference> SetReinforcementLevelAsync(byte level, CancellationToken cancellationToken);
    ValueTask<ItemReference> ResealAsync(byte nextCount, CancellationToken cancellationToken);
    ValueTask<ItemReference> SetEnchantmentAsync(int cardId, CancellationToken cancellationToken);
    ValueTask<ItemReference> SetForgingLevelAsync(byte level, CancellationToken cancellationToken);
    ValueTask<ItemReference> SetDimensionalAttributeAsync(byte type, ushort value, CancellationToken cancellationToken);
    ValueTask<ItemReference> SetMagicSealOptionsAsync(IReadOnlyList<RandomOptionState> options,
        CancellationToken cancellationToken);
    ValueTask<ItemReference> SetDurabilityAsync(ushort value, CancellationToken cancellationToken);
    ValueTask<ItemReference> DecreaseDurabilityAsync(ushort amount, CancellationToken cancellationToken);
    ValueTask<ItemReference> DestroyAsync(CancellationToken cancellationToken);
    /// <summary>Writes the growth weapon state carried in the item (experience and today's maintained fatigue).</summary>
    ValueTask<ItemReference> SetGrowthAsync(ushort experience, byte maintainedFatigue, CancellationToken cancellationToken);
    /// <summary>
    /// Turns this piece into another equipment template in place: same UID, same slot. The caller
    /// decides, through <paramref name="retain"/>, what the new template keeps from the old piece;
    /// it receives the current item with only the template already replaced.
    /// </summary>
    ValueTask<ItemReference> TransformAsync(int templateId, Func<ItemSnapshot, ItemSnapshot> retain,
        CancellationToken cancellationToken);
}
