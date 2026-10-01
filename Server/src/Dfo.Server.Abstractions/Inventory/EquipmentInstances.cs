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

public sealed record EquipmentInstanceFacts(CharacterId Owner, int ItemUid,
    BusinessSlot Slot, InventoryItemCoreState Core);

/// <summary>A transaction-bound identity. Rules decide the values these operations apply.</summary>
public interface IEquipmentInstance
{
    CharacterId Owner { get; }
    int ItemUid { get; }
    ValueTask<EquipmentInstanceFacts> ReadAsync(CancellationToken cancellationToken);
    ValueTask<EquipmentInstanceFacts> SetReinforcementLevelAsync(byte level, CancellationToken cancellationToken);
    ValueTask<EquipmentInstanceFacts> SetEnchantmentAsync(int cardId, CancellationToken cancellationToken);
    ValueTask<EquipmentInstanceFacts> SetForgingLevelAsync(byte level, CancellationToken cancellationToken);
    ValueTask<EquipmentInstanceFacts> SetDimensionalAttributeAsync(byte type, ushort value, CancellationToken cancellationToken);
    ValueTask<EquipmentInstanceFacts> SetMagicSealOptionsAsync(IReadOnlyList<RandomOptionState> options,
        CancellationToken cancellationToken);
    ValueTask<EquipmentInstanceFacts> SetDurabilityAsync(ushort value, CancellationToken cancellationToken);
    ValueTask<EquipmentInstanceFacts> DecreaseDurabilityAsync(ushort amount, CancellationToken cancellationToken);
    ValueTask<EquipmentInstanceFacts> DestroyAsync(CancellationToken cancellationToken);
}
