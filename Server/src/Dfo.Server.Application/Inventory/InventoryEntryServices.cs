using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class CharacterInventoryEntryService(ICharacterInventoryEntryRepository repository)
    : ICharacterInventoryEntryService
{
    public async ValueTask<CharacterInventoryEntryState> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var stored = await repository.LoadCharacterAsync(accountId, characterId, cancellationToken)
            .ConfigureAwait(false);
        return InventoryEntryProjection.ProjectCharacter(stored, accountId, characterId);
    }
}

internal sealed class AccountInventoryEntryService(IAccountInventoryEntryRepository repository)
    : IAccountInventoryEntryService
{
    public async ValueTask<AccountInventoryEntryState> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var stored = await repository.LoadAccountAsync(accountId, characterId, cancellationToken)
            .ConfigureAwait(false);
        return InventoryEntryProjection.ProjectAccount(stored, accountId, characterId);
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
                stored.AccountCargoState.Value32,
                stored.AccountCargoState.StoredItemCount),
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
            IReadOnlyList<InventoryStorageItem> source,
            CharacterId characterId)
    {
        ValidateItems(
            source,
            item => item.OwnerType == InventoryOwnerType.Character
                && item.OwnerId == characterId.Value
                && item.CharacterId == characterId
                && CharacterContainerOrder.Contains(item.ContainerType),
            "character inventory");
        return source
            .GroupBy(item => item.ContainerType)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<InventoryItemState>)group
                    .OrderBy(item => item.SlotIndex)
                    .ThenBy(item => item.StorageEntryId)
                    .Select(ToItemState)
                    .ToArray());
    }

    private static IReadOnlyList<InventoryItemState> NormalizeAccountCargoItems(
        IReadOnlyList<InventoryStorageItem> source,
        AccountId accountId)
    {
        ValidateItems(
            source,
            item => item.OwnerType == InventoryOwnerType.Account
                && item.OwnerId == accountId.Value
                && item.ContainerType == InventoryContainerType.AccountCargo,
            "account cargo");
        return source
            .OrderBy(item => item.SlotIndex)
            .ThenBy(item => item.StorageEntryId)
            .Select(ToItemState)
            .ToArray();
    }

    private static void ValidateItems(
        IReadOnlyList<InventoryStorageItem> source,
        Func<InventoryStorageItem, bool> ownsItem,
        string sourceName)
    {
        var slots = new HashSet<(InventoryContainerType ContainerType, int SlotIndex)>();
        foreach (var item in source)
        {
            if (!ownsItem(item) || item.SlotIndex < 0 || item.Core is null)
            {
                throw new InvalidDataException($"The {sourceName} contains an invalid item.");
            }

            if (!slots.Add((item.ContainerType, item.SlotIndex)))
            {
                throw new InvalidDataException(
                    $"The {sourceName} contains duplicate slot {item.ContainerType}/{item.SlotIndex}.");
            }
        }
    }

    private static InventoryItemState ToItemState(InventoryStorageItem item) =>
        new(item.StorageEntryId, item.SlotIndex, item.Core);

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
    /// <summary>Where an equipped creature sits in the equipment container.</summary>
    private const int CreatureEquipSlot = 24;

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
                if (item.SlotIndex == CreatureEquipSlot && item.Core.ItemKind == CreatureItemKind)
                    return item.Core.ItemId;
            }
        }

        return 0;
    }
}
