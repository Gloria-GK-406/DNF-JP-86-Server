using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public enum BusinessInventoryStoreKind { Item, Avatar, Creature, Medal }

public static class BusinessInventoryOwnership
{
    public static BusinessInventoryStoreKind StoreOf(BusinessInventorySpace space) => space switch
    {
        BusinessInventorySpace.QuickSlot or BusinessInventorySpace.CarriedEquipment
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
    IBusinessInventoryStore GetStore(ContainerOwner owner, BusinessInventoryStoreKind kind) =>
        owner.Scope == InventoryOwnerType.Character
            ? GetStore(new CharacterId(owner.Value), kind)
            : throw new NotSupportedException("This inventory provider does not support account owners.");
}

public interface IBusinessInventoryStore
{
    BusinessInventoryStoreKind Kind { get; }
    IBusinessItemContainer? FindContainer(BusinessInventorySpace space);
    IBusinessItemContainer GetContainer(BusinessInventorySpace space);
}

public interface IBusinessItemContainer
{
    BusinessContainerId Id { get; }
    ContainerId StorageId { get; }
    BusinessInventorySpace Space { get; }
    ItemTraits AcceptedTraits => ItemContainerAdmission.AcceptedBy(Space);
    ValueTask<int> OpenSlotCountAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(int.MaxValue);
    ValueTask<ContainerSlotItem?> ReadSlotAsync(int index, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(CancellationToken cancellationToken);
    ValueTask PutAsync(int index, InventoryItemCoreState core, CancellationToken cancellationToken);
    ValueTask PutTransferredAsync(int index, InventoryItemCoreState core,
        IBusinessItemContainer source, int sourceIndex, CancellationToken cancellationToken);
    ValueTask PutNewAsync(int index, InventoryItemCoreState core, InventoryItemCreationFacts facts,
        CancellationToken cancellationToken);
    ValueTask ClearAsync(int index, CancellationToken cancellationToken);
    ValueTask FlushAsync(CancellationToken cancellationToken);
    ValueTask<BusinessItemAdditionResult> AddAsync(int itemId, int count, CancellationToken cancellationToken);
}

public readonly record struct BusinessContainerId(BusinessInventorySpace Space, ContainerId StorageId);
public sealed record BusinessItemAdditionResult(IBusinessItemContainer Container, ItemPlacementOutcome Placement);
