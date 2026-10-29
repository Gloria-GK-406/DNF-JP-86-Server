using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class CharacterInventoryEntryDomainService(ICharacterInventoryEntryRepository repository,
    LegacyWornItemNormalizer? wornNormalizer = null, ICharacterContainerDomainService? containers = null)
    : ICharacterInventoryEntryDomainService
{
    public async ValueTask<CharacterInventoryEntryState> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        if (wornNormalizer is not null)
            await wornNormalizer.NormalizeAsync(characterId, cancellationToken).ConfigureAwait(false);
        return await ReadCurrentAsync(accountId, characterId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<CharacterInventoryEntryState> ReadCurrentAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        var stored = await repository.LoadCharacterAsync(accountId, characterId, cancellationToken)
            .ConfigureAwait(false);
        var result = InventoryEntryProjection.ProjectCharacter(stored, accountId, characterId);
        if (containers?.FindContainer(characterId, InventoryContainerType.Main) is { IsPersistent: false } current)
        {
            var items = await current.GetAllAsync(cancellationToken).ConfigureAwait(false);
            result = result with { Containers = result.Containers.Select(value => value.ContainerType == InventoryContainerType.Main
                ? new InventoryContainerState(value.ContainerType, current.Shape.Parameter,
                    items.Select(item => new InventoryItemState(item.ItemUid, item.StorageSlotIndex, item.Snapshot.ToCore())).ToArray())
                : value).ToArray() };
        }
        return result;
    }
}

internal sealed class AccountInventoryEntryDomainService(IAccountInventoryEntryRepository repository,
    ICharacterContainerDomainService? containers = null)
    : IAccountInventoryEntryDomainService
{
    public async ValueTask<AccountInventoryEntryState> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var stored = await repository.LoadAccountAsync(accountId, characterId, cancellationToken)
            .ConfigureAwait(false);
        var result = InventoryEntryProjection.ProjectAccount(stored, accountId, characterId);
        if (containers?.FindContainer(characterId, InventoryContainerType.SharedMaterial) is { IsPersistent: false } current)
        {
            var contents = await current.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var counts = contents.ToDictionary(item => item.StorageSlotIndex, item => item.Count);
            result = result with { VirtualResources = AccountMaterialCatalog.Definitions.Select((material, index) =>
                new InventoryRuntimeValueState(material.VirtualSlotIndex, material.ItemId, counts.GetValueOrDefault(index))).ToArray() };
        }
        return result;
    }
}

internal static class InventoryEntryProjection
{
    private static readonly InventoryContainerType[] CharacterContainerOrder =
    [
        InventoryContainerType.Main,
        InventoryContainerType.Avatar,
        InventoryContainerType.PersonalCargo,
        InventoryContainerType.Equipment,
        InventoryContainerType.Pet,
    ];

    public static CharacterInventoryEntryState ProjectCharacter(
        CharacterInventoryEntryStorageRecord stored,
        AccountId accountId,
        CharacterId characterId)
    {
        if (stored.AccountId != accountId || stored.CharacterId != characterId)
        {
            throw new InvalidDataException(
                "Character inventory storage result does not match the requested owner.");
        }

        var parameters = NormalizeParameters(stored.CharacterContainerParameters, characterId);
        var characterItems = NormalizeCharacterItems(stored.CharacterItems, characterId);
        var containers = CharacterContainerOrder
            .Select(containerType => new InventoryContainerState(
                containerType,
                parameters.TryGetValue(containerType, out var parameter)
                    ? parameter
                    : GetDefaultParameter(containerType),
                characterItems.TryGetValue(containerType, out var items) ? items : []))
            .ToArray();
        var characterVirtualResources = NormalizeRuntimeValues(stored.CharacterVirtualResources);
        var itemValues = NormalizeItemValues(stored.CharacterItemValues, characterId);
        var equipmentLocks = NormalizeEquipmentLocks(stored.EquipmentLocks, characterId);
        var rentals = NormalizeRentals(stored.CharacterRentals, characterId);
        if (equipmentLocks.Any(item => item.ContainerType == InventoryContainerType.AccountCargo))
        {
            throw new InvalidDataException(
                "Character inventory storage contains an account cargo equipment lock.");
        }

        return new CharacterInventoryEntryState(
            characterId,
            containers,
            characterVirtualResources,
            itemValues,
            equipmentLocks,
            rentals,
            stored.GoldLimits);
    }

    public static AccountInventoryEntryState ProjectAccount(
        AccountInventoryEntryStorageRecord stored,
        AccountId accountId,
        CharacterId characterId)
    {
        if (stored.AccountId != accountId || stored.CharacterId != characterId)
        {
            throw new InvalidDataException(
                "Account inventory storage result does not match the requested owner.");
        }

        var accountCargoItems = NormalizeAccountCargoItems(stored.AccountCargoItems, accountId);
        if (stored.AccountCargoState.AccountId != accountId)
        {
            throw new InvalidDataException("Account cargo state does not match the requested account.");
        }

        var accountVirtualResources = NormalizeRuntimeValues(stored.AccountVirtualResources);
        var accountCargoEquipmentLocks = NormalizeEquipmentLocks(stored.EquipmentLocks, characterId);
        if (accountCargoEquipmentLocks.Any(
                item => item.ContainerType != InventoryContainerType.AccountCargo))
        {
            throw new InvalidDataException(
                "Account inventory storage contains a non-cargo equipment lock.");
        }

        var cargoContainer = new InventoryContainerState(
            InventoryContainerType.AccountCargo,
            stored.AccountCargoState.SelectionKey,
            accountCargoItems);
        return new AccountInventoryEntryState(
            accountId,
            new AccountCargoEntryState(
                accountId,
                cargoContainer,
                accountCargoEquipmentLocks,
                stored.AccountCargoState.SelectionKey,
                stored.AccountCargoState.Value32),
            accountVirtualResources);
    }

    private static IReadOnlyDictionary<InventoryContainerType, ushort> NormalizeParameters(
        IReadOnlyList<InventoryContainerParameter> source,
        CharacterId characterId)
    {
        var parameters = new Dictionary<InventoryContainerType, ushort>();
        foreach (var parameter in source)
        {
            if (parameter.CharacterId != characterId
                || !CharacterContainerOrder.Contains(parameter.ContainerType)
                || !parameters.TryAdd(parameter.ContainerType, parameter.Value))
            {
                throw new InvalidDataException(
                    "Character inventory container metadata is invalid or duplicated.");
            }
        }

        return parameters;
    }

    private static IReadOnlyDictionary<InventoryContainerType, IReadOnlyList<InventoryItemState>>
        NormalizeCharacterItems(
            IReadOnlyList<ItemReference> source,
            CharacterId characterId)
    {
        ValidateItems(
            source,
            item => item.ContainerId.TryGetCharacterOwner(out var owner, out var type)
                && owner == characterId && CharacterContainerOrder.Contains(type),
            "character inventory");
        return source
            .GroupBy(StorageType)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<InventoryItemState>)group
                    .OrderBy(item => item.SlotIndex)
                    .Select(ToItemState)
                    .ToArray());
    }

    private static IReadOnlyList<InventoryItemState> NormalizeAccountCargoItems(
        IReadOnlyList<ItemReference> source,
        AccountId accountId)
    {
        ValidateItems(
            source,
            item => item.ContainerId.TryGetAccountCargoOwner(out var owner) && owner == accountId,
            "account cargo");
        return source
            .OrderBy(item => item.SlotIndex)
            .Select(ToItemState)
            .ToArray();
    }

    private static void ValidateItems(
        IReadOnlyList<ItemReference> source,
        Func<ItemReference, bool> ownsItem,
        string sourceName)
    {
        var slots = new HashSet<(InventoryContainerType ContainerType, int SlotIndex)>();
        foreach (var item in source)
        {
            if (!ownsItem(item) || item.SlotIndex < 0 || item.Snapshot is null)
            {
                throw new InvalidDataException($"The {sourceName} contains an invalid item.");
            }

            if (!slots.Add((StorageType(item), item.SlotIndex)))
            {
                throw new InvalidDataException(
                    $"The {sourceName} contains duplicate slot {StorageType(item)}/{item.SlotIndex}.");
            }
        }
    }

    /// <remarks>
    /// The two orderings above sort by slot alone. They used to break a tie on the storage row
    /// key, which was a tie that cannot happen -- <see cref="ValidateItems"/> has already refused
    /// two items in one container's slot -- and which asked a pile for a mark it does not have.
    /// </remarks>
    private static InventoryContainerType StorageType(ItemReference item) =>
        item.ContainerId.TryGetOwner(out _, out var type) ? type
            : throw new InvalidDataException("Inventory item has an invalid container.");

    private static InventoryItemState ToItemState(ItemReference item) =>
        new(item.ItemUid, item.SlotIndex, item.Snapshot.ToCore(), item.Snapshot.AvatarDetail);

    private static IReadOnlyList<InventoryRuntimeValueState> NormalizeRuntimeValues(
        IReadOnlyList<InventoryRuntimeValueState> source)
    {
        var slots = new HashSet<int>();
        foreach (var value in source)
        {
            if (value.SlotIndex < 0
                || value.ItemId < 0
                || value.Value < 0
                || !slots.Add(value.SlotIndex))
            {
                throw new InvalidDataException("Character inventory runtime values are invalid or duplicated.");
            }
        }

        return source.OrderBy(value => value.SlotIndex).ToArray();
    }

    private static CharacterItemValueEntryState NormalizeItemValues(
        IReadOnlyList<InventoryItemValueStorageRecord> source,
        CharacterId characterId)
    {
        var seen = new HashSet<(InventoryItemValueListType ListType, int SortOrder)>();
        foreach (var item in source)
        {
            if (item.CharacterId != characterId
                || item.SortOrder < 0
                || item.ListType is not InventoryItemValueListType.Cooldown
                    and not InventoryItemValueListType.Effect
                || !seen.Add((item.ListType, item.SortOrder)))
            {
                throw new InvalidDataException(
                    "Character item values have invalid ownership, order, or duplicate entries.");
            }
        }

        return new CharacterItemValueEntryState(
            source
                .Where(item => item.ListType == InventoryItemValueListType.Cooldown)
                .OrderBy(item => item.SortOrder)
                .Select(ToItemValueState)
                .ToArray(),
            source
                .Where(item => item.ListType == InventoryItemValueListType.Effect)
                .OrderBy(item => item.SortOrder)
                .Select(ToItemValueState)
                .ToArray());
    }

    private static IReadOnlyList<InventoryEquipmentLockState> NormalizeEquipmentLocks(
        IReadOnlyList<InventoryEquipmentLockStorageRecord> source,
        CharacterId characterId)
    {
        var lockIds = new HashSet<int>();
        var slots = new HashSet<(InventoryContainerType ContainerType, int SlotIndex)>();
        foreach (var item in source)
        {
            if (item.CharacterId != characterId
                || item.EquipmentLockId is < 1 or > byte.MaxValue
                || item.SlotIndex < 0
                || item.State is < byte.MinValue or > byte.MaxValue
                || item.RemainingSeconds < 0
                || !Enum.IsDefined(item.ContainerType)
                || !lockIds.Add(item.EquipmentLockId)
                || !slots.Add((item.ContainerType, item.SlotIndex)))
            {
                throw new InvalidDataException(
                    "Character equipment locks have invalid ownership, identity, slot, or runtime state.");
            }
        }

        return source
            .OrderBy(item => item.EquipmentLockId)
            .Select(item => new InventoryEquipmentLockState(
                checked((byte)item.EquipmentLockId),
                item.ContainerType,
                item.SlotIndex,
                checked((byte)item.State),
                item.RemainingSeconds))
            .ToArray();
    }

    private static InventoryItemValueState ToItemValueState(InventoryItemValueStorageRecord item) =>
        new(item.SortOrder, item.ItemId, item.Value);

    private static CharacterRentalEntryState NormalizeRentals(
        IReadOnlyList<CharacterRentalStorageRecord> source,
        CharacterId characterId)
    {
        var itemIds = new HashSet<long>();
        var inventoryTemplateIds = new HashSet<long>();
        foreach (var item in source)
        {
            if (item.CharacterId != characterId
                || item.ItemId is < 1 or > uint.MaxValue
                || item.InventoryTemplateId is < 0 or > uint.MaxValue
                || item.ExpireTime is < 1 or > uint.MaxValue
                || !itemIds.Add(item.ItemId)
                || (item.InventoryTemplateId != 0
                    && !inventoryTemplateIds.Add(item.InventoryTemplateId)))
            {
                throw new InvalidDataException(
                    "Character rental state has invalid ownership, identity, expiration, or duplicates.");
            }
        }

        return new CharacterRentalEntryState(source
            .OrderBy(item => item.ItemId)
            .ThenBy(item => item.InventoryTemplateId)
            .Select(item => new CharacterRentalItemState(
                checked((uint)item.ItemId),
                checked((uint)item.InventoryTemplateId),
                checked((uint)item.ExpireTime)))
            .ToArray());
    }

    private static ushort GetDefaultParameter(InventoryContainerType containerType) =>
        containerType == InventoryContainerType.PersonalCargo ? (ushort)8 : (ushort)0;
}

internal sealed class EquippedCreatureQuery : IEquippedCreatureQuery
{
    /// <summary>The item kind the legacy inventory uses for creatures.</summary>
    private const byte CreatureItemKind = 5;

    public int GetItemId(CharacterInventoryEntryState inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        foreach (var container in inventory.Containers)
        {
            if (container.ContainerType is not InventoryContainerType.Equipment)
                continue;

            foreach (var item in container.Items)
            {
                if (item.Core.ItemKind == CreatureItemKind
                    && LegacyInventoryStorageCoordinates.ToBusiness(
                        new(InventoryContainerType.Equipment, item.SlotIndex), item.Core.ItemKind)
                    == new BusinessSlot(BusinessInventorySpace.WornCreature, 0))
                    return item.Core.ItemId;
            }
        }

        return 0;
    }
}
