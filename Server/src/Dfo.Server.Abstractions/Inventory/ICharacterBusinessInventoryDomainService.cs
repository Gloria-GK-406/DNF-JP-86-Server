using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public enum BusinessInventoryStoreKind { Item, Avatar, Creature, Medal }

public static class BusinessInventoryOwnership
{
    public static BusinessInventoryStoreKind StoreOf(BusinessInventorySpace space) => space switch
    {
        BusinessInventorySpace.QuickSlot or BusinessInventorySpace.TowerQuickSlot or BusinessInventorySpace.CarriedEquipment
            or BusinessInventorySpace.Consumable or BusinessInventorySpace.Material
            or BusinessInventorySpace.Quest or BusinessInventorySpace.ExpertMaterial
            or BusinessInventorySpace.PersonalCargo
            or BusinessInventorySpace.WornEquipment or BusinessInventorySpace.WornEquipmentExtension
            or BusinessInventorySpace.AccountCargo or BusinessInventorySpace.SharedMaterial => BusinessInventoryStoreKind.Item,
        BusinessInventorySpace.Avatar or BusinessInventorySpace.AvatarEmblem
            or BusinessInventorySpace.WornAvatar => BusinessInventoryStoreKind.Avatar,
        BusinessInventorySpace.CreatureInventory or BusinessInventorySpace.CreatureEquipment
            or BusinessInventorySpace.CreatureConsumable or BusinessInventorySpace.WornCreature
            or BusinessInventorySpace.WornCreatureEquipment => BusinessInventoryStoreKind.Creature,
        BusinessInventorySpace.GuildMedal or BusinessInventorySpace.GuardianGem => BusinessInventoryStoreKind.Medal,
        _ => throw new InventorySlotMappingException(new BusinessSlot(space, 0)),
    };
}

public interface ICharacterBusinessInventoryDomainService
{
    IBusinessInventoryStore GetStore(CharacterId characterId, BusinessInventoryStoreKind kind);
    /// <summary>Explicit persistent access for offline delivery and entry loading; does not register an online container.</summary>
    IBusinessInventoryStore GetPersistentStore(ContainerOwner owner, BusinessInventoryStoreKind kind) =>
        throw new NotSupportedException("This inventory provider does not support explicit persistent access.");
}

public interface IBusinessInventoryStore
{
    BusinessInventoryStoreKind Kind { get; }
    IBusinessItemContainer? FindContainer(BusinessInventorySpace space, bool isDefault = false);
    IBusinessItemContainer GetContainer(BusinessInventorySpace space, bool isDefault = false);
}

public interface IBusinessItemContainer
{
    BusinessContainerId Id { get; }
    ContainerId StorageId { get; }
    bool IsDefault => true;
    bool IsPersistent => true;
    BusinessInventorySpace Space { get; }
    ItemAdmissionRule Admission => ItemContainerAdmission.For(Space);
    ValueTask<int> OpenSlotCountAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(int.MaxValue);
    ValueTask<IEquipmentInstance?> GetEquipmentAsync(int index, CancellationToken ct);
    ValueTask<ItemReference?> GetAsync(int index, CancellationToken ct);
    ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(CancellationToken ct);
    ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct);
    ValueTask<ItemReference> PutAsync(int index, ItemRecord record, CancellationToken ct);
    ValueTask<ItemReference> UpdateAsync(ItemReference reference, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct);
    ValueTask FlushAsync(CancellationToken cancellationToken);
    ValueTask<BusinessItemAdditionResult> AddAsync(int itemId, int count, CancellationToken cancellationToken);
}

public readonly record struct BusinessContainerId(BusinessInventorySpace Space, ContainerId StorageId);
public sealed record BusinessItemAdditionResult(IBusinessItemContainer Container, ItemPlacementOutcome Placement);
