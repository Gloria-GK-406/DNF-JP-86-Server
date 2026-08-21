using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// What a character and its account are carrying, put together from the tables that hold it.
/// </summary>
/// <remarks>
/// Fourteen tables between the two loads, and what makes them one inventory is not in any of them:
/// which containers may appear where, that an avatar item without its detail row is not an avatar,
/// that the locks table is read whole by both loads and then divided between them, that two
/// rental tables are one list, and that a character with nothing in the packed tables but something
/// in the old ones has not been brought across and must be refused rather than entered empty.
/// Every one of those is a decision about inventories, so all of them are here; each table only
/// knows how to read its own rows.
///
/// The refusals are the load's, not the tables'. A row that names the wrong owner is a row the
/// table can read perfectly well -- whether it may be used is this one's question.
/// </remarks>
internal sealed class InventoryEntryAssembly(
    ICharacterTable characters,
    IAccountTable accounts,
    ICharacterItemTable characterItems,
    IAccountCargoItemTable cargoItems,
    ICharacterAvatarDetailTable avatarDetails,
    ICharacterContainerStateTable containerStates,
    IAccountCargoStateTable cargoStates,
    ICharacterGoldLimitTable goldLimits,
    ICharacterItemValueTable itemValues,
    ICharacterItemLockTable itemLocks,
    ICharacterRentalTable rentals,
    ILegacyRentalTable legacyRentals,
    ILegacyCharacterItemTable legacyCharacterItems,
    ILegacyAccountCargoTable legacyAccountCargo)
    : ICharacterInventoryEntryRepository, IAccountInventoryEntryRepository,
        IEntryInventoryLeasePersistence
{
    /// <summary>The six cube fragments, in the slots and under the ids the client knows.</summary>
    private static readonly int[] CubeItemIds = [3033, 3034, 3035, 3036, 3037, 3262];

    private const int FirstCubeSlot = 354;

    public async ValueTask<CharacterInventoryEntryStorageRecord> LoadCharacterAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        ValidateIdentifiers(accountId, characterId);
        await RequireOwnershipAsync(accountId, characterId, cancellationToken).ConfigureAwait(false);

        var items = await ReadCharacterItemsAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        if (items.Count == 0
            && await legacyCharacterItems.AnyAsync(characterId, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidDataException(
                $"Legacy character inventory exists for character {characterId.Value}; "
                + "migration required before entry loading.");
        }

        var containerParameters = await ReadContainerParametersAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var virtualResources = CharacterVirtualResources(items);
        var values = await ReadItemValuesAsync(characterId, cancellationToken).ConfigureAwait(false);
        var locks = await ReadLocksAsync(characterId, cancellationToken).ConfigureAwait(false);
        var rented = await ReadRentalsAsync(characterId, cancellationToken).ConfigureAwait(false);
        var limits = await ReadGoldLimitsAsync(characterId, cancellationToken).ConfigureAwait(false);

        RequireUniqueSlots(items, "character inventory");
        return new CharacterInventoryEntryStorageRecord(
            accountId,
            characterId,
            items,
            containerParameters,
            virtualResources,
            values,
            [.. locks.Where(item => item.ContainerType != InventoryContainerType.AccountCargo)],
            rented,
            limits);
    }

    public async ValueTask<AccountInventoryEntryStorageRecord> LoadAccountAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        ValidateIdentifiers(accountId, characterId);
        await RequireOwnershipAsync(accountId, characterId, cancellationToken).ConfigureAwait(false);

        var items = await ReadCargoItemsAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (items.Count == 0
            && await legacyAccountCargo.AnyAsync(accountId, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException(
                $"Legacy account cargo exists for account {accountId.Value}; "
                + "migration required before entry loading.");
        }

        var cargoState = await ReadCargoStateAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        var virtualResources = await AccountVirtualResourcesAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        var locks = await ReadLocksAsync(characterId, cancellationToken).ConfigureAwait(false);

        RequireUniqueSlots(items, "account cargo");
        return new AccountInventoryEntryStorageRecord(
            accountId,
            characterId,
            items,
            cargoState,
            virtualResources,
            [.. locks.Where(item => item.ContainerType == InventoryContainerType.AccountCargo)]);
    }

    public async ValueTask SaveAsync(
        EntryInventoryLeaseSnapshot lease, CancellationToken cancellationToken)
    {
        if (!lease.IsDirty || lease.Inventory.CharacterId != lease.Session.CharacterId)
        {
            throw new InvalidDataException(
                "Only an exact dirty character inventory lease can be saved.");
        }

        if (lease.Inventory.Containers.Any(
                value => value.ContainerType == InventoryContainerType.AccountCargo))
        {
            throw new InvalidDataException("A character item lease cannot contain account cargo.");
        }

        var characterId = lease.Session.CharacterId;
        await RequireOwnershipAsync(lease.Session.AccountId, characterId, cancellationToken)
            .ConfigureAwait(false);

        var present = lease.Inventory.Containers
            .Where(value => value.ContainerType != InventoryContainerType.AccountCargo)
            .SelectMany(value => value.Items).Where(value => value.StorageEntryId > 0)
            .Select(value => value.StorageEntryId).Distinct().ToArray();
        await characterItems.DeleteOwnExceptAsync(characterId, present, cancellationToken)
            .ConfigureAwait(false);

        var avatars = lease.Inventory.Containers.SelectMany(value => value.Items)
            .Where(value => value.Core.ItemKind == 8).ToArray();
        if (avatars.Any(value => value.Core.Value <= 0 || value.AvatarDetail is null
                                 || value.AvatarDetail.JewelSocket.Length != 30))
        {
            throw new InvalidDataException(
                "A dirty avatar lease requires its exact 30-byte detail row.");
        }

        await avatarDetails.DeleteOwnExceptAsync(
            characterId, [.. avatars.Select(value => value.Core.Value)], cancellationToken)
            .ConfigureAwait(false);

        foreach (var container in lease.Inventory.Containers.Where(
                     value => value.ContainerType != InventoryContainerType.AccountCargo))
        {
            foreach (var item in container.Items)
            {
                var touched = item.StorageEntryId > 0
                    ? await characterItems.UpdateOwnAsync(
                        item.StorageEntryId, characterId, (byte)container.ContainerType,
                        item.SlotIndex, item.Core, cancellationToken).ConfigureAwait(false)
                    : await characterItems.InsertOwnAsync(
                        characterId, (byte)container.ContainerType, item.SlotIndex,
                        item.Core, cancellationToken).ConfigureAwait(false);
                if (touched != 1)
                {
                    throw new InvalidDataException(
                        $"Dirty inventory item {item.StorageEntryId} is stale or missing.");
                }

                if (item.Core.ItemKind != 8)
                {
                    continue;
                }

                if (await avatarDetails.UpsertOwnAsync(
                        characterId, item.Core.Value, item.Core.ItemId, item.AvatarDetail!,
                        cancellationToken).ConfigureAwait(false) != 1)
                {
                    throw new InvalidDataException("Dirty avatar detail was not persisted.");
                }
            }
        }
    }

    private async ValueTask RequireOwnershipAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        if (!await characters.BelongsToAsync(characterId, accountId, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidDataException(
                $"Character {characterId.Value} does not belong to account {accountId.Value}.");
        }
    }

    private async ValueTask<IReadOnlyList<InventoryStorageItem>> ReadCharacterItemsAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var items = new List<InventoryStorageItem>();
        foreach (var row in await characterItems.ReadByCharacterAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            var ownerType = ParseOwnerType(row.OwnerScope);
            var storedCharacterId = row.CharacterId is { } value ? new CharacterId(value) : (CharacterId?)null;
            var containerType = ParseContainerType(row.ListType, allowAccountCargo: false);
            if (ownerType != InventoryOwnerType.Character
                || row.OwnerId != characterId.Value
                || storedCharacterId != characterId)
            {
                throw new InvalidDataException(
                    $"Inventory row {row.ItemUid} has invalid character ownership.");
            }

            RequireSlot(row.SlotIndex, row.ItemUid);
            items.Add(new InventoryStorageItem(
                row.ItemUid, ownerType, row.OwnerId, storedCharacterId, containerType,
                row.SlotIndex, row.Core));
        }

        return await WithAvatarDetailsAsync(items, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<InventoryStorageItem>> ReadCargoItemsAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        var items = new List<InventoryStorageItem>();
        foreach (var row in await cargoItems.ReadByAccountAsync(accountId, cancellationToken)
                     .ConfigureAwait(false))
        {
            var storedCharacterId = row.CharacterId is { } value ? new CharacterId(value) : (CharacterId?)null;
            var containerType = ParseContainerType(row.ListType, allowAccountCargo: true);
            if (row.AccountId != accountId.Value
                || containerType != InventoryContainerType.AccountCargo)
            {
                throw new InvalidDataException(
                    $"Inventory row {row.ItemUid} has invalid account cargo ownership.");
            }

            RequireSlot(row.SlotIndex, row.ItemUid);
            items.Add(new InventoryStorageItem(
                row.ItemUid, InventoryOwnerType.Account, row.AccountId, storedCharacterId,
                containerType, row.SlotIndex, row.Core));
        }

        // No avatar details here, unlike the character's own items. A detail row belongs to a
        // character, and one of these belongs to an account, so there is nothing that could
        // match: an avatar sitting in the cargo is carried with no detail at all.
        return items;
    }

    /// <summary>An avatar item is not one until its detail row is found and agrees with it.</summary>
    /// <remarks>
    /// Done over the whole list once it has been read, not row by row as it is read. Which of the
    /// two problems a caller is told about when a list has both a row with the wrong owner and an
    /// avatar with no detail depends on that, so it is kept as it was.
    /// </remarks>
    private async ValueTask<IReadOnlyList<InventoryStorageItem>> WithAvatarDetailsAsync(
        IReadOnlyList<InventoryStorageItem> items, CancellationToken cancellationToken)
    {
        var result = new List<InventoryStorageItem>(items.Count);
        foreach (var item in items)
        {
            result.Add(await WithAvatarDetailAsync(item, cancellationToken).ConfigureAwait(false));
        }

        return result;
    }

    private async ValueTask<InventoryStorageItem> WithAvatarDetailAsync(
        InventoryStorageItem item, CancellationToken cancellationToken)
    {
        if (item.Core.ItemKind != 8)
        {
            return item;
        }

        if (item.Core.Value <= 0)
        {
            throw new InvalidDataException(
                $"Avatar inventory row {item.StorageEntryId} has no avatar UID.");
        }

        var detail = await avatarDetails.FindAsync(item.Core.Value, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidDataException($"Avatar detail {item.Core.Value} is unavailable.");
        if (detail.OwnerId != item.OwnerId
            || detail.CharacterId != item.CharacterId?.Value
            || detail.ItemId != item.Core.ItemId
            || detail.JewelSocket.Length != 30
            || detail.Color1 is < 0 or > ushort.MaxValue
            || detail.Color2 is < 0 or > ushort.MaxValue)
        {
            throw new InvalidDataException(
                $"Avatar detail {item.Core.Value} is malformed or has the wrong owner.");
        }

        return item with
        {
            AvatarDetail = new InventoryAvatarDetailState(
                checked((int)detail.ExpireDate), checked((int)detail.ClearAvatarId),
                [.. detail.JewelSocket], checked((ushort)detail.Color1),
                checked((ushort)detail.Color2), checked((int)detail.DeleteDate)),
        };
    }

    private async ValueTask<IReadOnlyList<InventoryContainerParameter>> ReadContainerParametersAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var parameters = new List<InventoryContainerParameter>();
        var seen = new HashSet<InventoryContainerType>();
        foreach (var row in await containerStates.ReadByCharacterAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            var storedCharacterId = new CharacterId(row.CharacterId);
            var containerType = ParseContainerType(row.ListType, allowAccountCargo: false);
            if (storedCharacterId != characterId || !seen.Add(containerType))
            {
                throw new InvalidDataException(
                    "Character container metadata is duplicated or has the wrong owner.");
            }

            parameters.Add(new InventoryContainerParameter(
                storedCharacterId, containerType,
                RequireUInt16(row.Value, "container parameter")));
        }

        return parameters;
    }

    private async ValueTask<AccountCargoStorageState> ReadCargoStateAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        var rows = await cargoStates.ReadByAccountAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return new AccountCargoStorageState(accountId, 0, 0, 0);
        }

        var row = rows[0];
        if (row.AccountId != accountId.Value)
        {
            throw new InvalidDataException("Account cargo metadata has the wrong owner.");
        }

        // The first row is made sense of before a second one is objected to, which is the order
        // these were in: a row that is both out of range and duplicated says so about the range.
        var state = new AccountCargoStorageState(
            new AccountId(row.AccountId),
            RequireUInt16(row.SelectionKey, "account cargo selection key"),
            row.Value32,
            RequireUInt16(row.ItemCount, "account cargo item count"));
        return rows.Count > 1
            ? throw new InvalidDataException("Account cargo metadata is duplicated.")
            : state;
    }

    private async ValueTask<InventoryGoldLimitState> ReadGoldLimitsAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var row = await goldLimits.FindAsync(characterId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return new InventoryGoldLimitState(0, 0);
        }

        return row.CarryLimit < 0 || row.AuctionLimit < 0
            ? throw new InvalidDataException("Character gold limits cannot be negative.")
            : new InventoryGoldLimitState(row.CarryLimit, row.AuctionLimit);
    }

    /// <summary>
    /// The first three slots of the main list are counters rather than items.
    /// </summary>
    /// <remarks>
    /// They are read out of the items already loaded rather than from the database again, which
    /// is why this one takes no connection and asks no table anything.
    /// </remarks>
    private static IReadOnlyList<InventoryRuntimeValueState> CharacterVirtualResources(
        IReadOnlyList<InventoryStorageItem> items) =>
        [.. items
            .Where(item => item.ContainerType == InventoryContainerType.Main
                && item.SlotIndex is >= 0 and <= 2)
            .Select(item => new InventoryRuntimeValueState(
                item.SlotIndex, item.Core.ItemId, Math.Max(0, item.Core.Value)))
            .OrderBy(value => value.SlotIndex)];

    private async ValueTask<IReadOnlyList<InventoryRuntimeValueState>> AccountVirtualResourcesAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        var row = await accounts.FindCubeFragmentsAsync(accountId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidDataException(
                $"Account {accountId.Value} is missing while loading inventory values.");
        int[] counts = [row.Black, row.White, row.Red, row.Blue, row.Clear, row.Gold];
        var values = new List<InventoryRuntimeValueState>();
        for (var index = 0; index < counts.Length; index++)
        {
            if (counts[index] < 0)
            {
                throw new InvalidDataException("Account cube fragment counts cannot be negative.");
            }

            if (counts[index] > 0)
            {
                values.Add(new InventoryRuntimeValueState(
                    FirstCubeSlot + index, CubeItemIds[index], counts[index]));
            }
        }

        return [.. values.OrderBy(value => value.SlotIndex)];
    }

    private async ValueTask<IReadOnlyList<InventoryItemValueStorageRecord>> ReadItemValuesAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var values = new List<InventoryItemValueStorageRecord>();
        var seen = new HashSet<(InventoryItemValueListType ListType, int SortOrder)>();
        foreach (var row in await itemValues.ReadByCharacterAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            var storedCharacterId = new CharacterId(row.CharacterId);
            var listType = row.ListKind switch
            {
                "cooltime" => InventoryItemValueListType.Cooldown,
                "effect" => InventoryItemValueListType.Effect,
                var value => throw new InvalidDataException(
                    $"Character item value list kind '{value}' is invalid."),
            };
            if (storedCharacterId != characterId
                || row.SortOrder < 0
                || !seen.Add((listType, row.SortOrder)))
            {
                throw new InvalidDataException(
                    "Character item values have invalid ownership, order, or duplicate entries.");
            }

            values.Add(new InventoryItemValueStorageRecord(
                storedCharacterId, listType, row.SortOrder, row.ItemId, row.Value));
        }

        return values;
    }

    private async ValueTask<IReadOnlyList<InventoryEquipmentLockStorageRecord>> ReadLocksAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var locks = new List<InventoryEquipmentLockStorageRecord>();
        var lockIds = new HashSet<int>();
        var slots = new HashSet<(InventoryContainerType ContainerType, int SlotIndex)>();
        foreach (var row in await itemLocks.ReadByCharacterAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            var storedCharacterId = new CharacterId(row.CharacterId);
            var containerType = ParseLockContainerType(row.InventoryListType);
            if (storedCharacterId != characterId
                || row.EquipmentLockId is < 1 or > byte.MaxValue
                || row.SlotIndex < 0
                || row.State is < byte.MinValue or > byte.MaxValue
                || row.RemainingSeconds < 0
                || !lockIds.Add(row.EquipmentLockId)
                || !slots.Add((containerType, row.SlotIndex)))
            {
                throw new InvalidDataException(
                    "Character equipment locks have invalid ownership, identity, slot, "
                    + "or runtime state.");
            }

            locks.Add(new InventoryEquipmentLockStorageRecord(
                storedCharacterId, row.EquipmentLockId, containerType, row.SlotIndex, row.State,
                row.RemainingSeconds));
        }

        return locks;
    }

    /// <summary>
    /// The two rental tables are one list of rentals.
    /// </summary>
    /// <remarks>
    /// Read in this order and checked as they arrive, because the duplicate check spans both: an
    /// item that appears in each of them is a duplicate, and reading them separately then merging
    /// would let one of them through.
    /// </remarks>
    private async ValueTask<IReadOnlyList<CharacterRentalStorageRecord>> ReadRentalsAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var accepted = new List<CharacterRentalStorageRecord>();
        var itemIds = new HashSet<long>();
        var templateIds = new HashSet<long>();
        foreach (var row in
                 (await rentals.ReadByCharacterAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
                 .Concat(await legacyRentals.ReadByCharacterAsync(characterId, cancellationToken)
                     .ConfigureAwait(false)))
        {
            var storedCharacterId = new CharacterId(row.CharacterId);
            if (storedCharacterId != characterId
                || row.ItemId is < 1 or > uint.MaxValue
                || row.InventoryTemplateId is < 0 or > uint.MaxValue
                || row.ExpireTime is < 1 or > uint.MaxValue
                || !itemIds.Add(row.ItemId)
                || (row.InventoryTemplateId != 0 && !templateIds.Add(row.InventoryTemplateId)))
            {
                throw new InvalidDataException(
                    "Character rental state has invalid ownership, identity, expiration, "
                    + "or duplicates.");
            }

            accepted.Add(new CharacterRentalStorageRecord(
                storedCharacterId, row.ItemId, row.InventoryTemplateId, row.ExpireTime));
        }

        return [.. accepted.OrderBy(item => item.ItemId).ThenBy(item => item.InventoryTemplateId)];
    }

    private static void ValidateIdentifiers(AccountId accountId, CharacterId characterId)
    {
        if (accountId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Account ID must be positive.");
        }

        if (characterId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(characterId), "Character ID must be positive.");
        }
    }

    private static InventoryOwnerType ParseOwnerType(string value) => value switch
    {
        "character" => InventoryOwnerType.Character,
        "account" => InventoryOwnerType.Account,
        _ => throw new InvalidDataException($"Inventory owner scope '{value}' is invalid."),
    };

    private static InventoryContainerType ParseContainerType(int value, bool allowAccountCargo) =>
        value switch
        {
            0 => InventoryContainerType.Main,
            1 => InventoryContainerType.Avatar,
            2 => InventoryContainerType.PersonalCargo,
            3 => InventoryContainerType.Equipment,
            7 => InventoryContainerType.Pet,
            12 when allowAccountCargo => InventoryContainerType.AccountCargo,
            _ => throw new InvalidDataException(
                $"Inventory container type {value} is not an entry container."),
        };

    private static InventoryContainerType ParseLockContainerType(int value) => value switch
    {
        0 => InventoryContainerType.Main,
        1 => InventoryContainerType.Avatar,
        2 => InventoryContainerType.PersonalCargo,
        3 => InventoryContainerType.Equipment,
        7 => InventoryContainerType.Pet,
        12 => InventoryContainerType.AccountCargo,
        19 => InventoryContainerType.TitleBookGeneral,
        20 => InventoryContainerType.TitleBookSpecific,
        21 => InventoryContainerType.TitleBookPvp,
        22 => InventoryContainerType.TitleBookDespair,
        23 => InventoryContainerType.TitleBookEvent,
        29 => InventoryContainerType.QuickSlot,
        33 => InventoryContainerType.KnightShieldEquipped,
        34 => InventoryContainerType.KnightShieldCatalog,
        _ => throw new InvalidDataException($"Equipment lock container type {value} is invalid."),
    };

    private static ushort RequireUInt16(int value, string name) =>
        value is < ushort.MinValue or > ushort.MaxValue
            ? throw new InvalidDataException(
                $"Stored {name} {value} is outside the unsigned 16-bit range.")
            : (ushort)value;

    private static void RequireSlot(int slotIndex, long storageEntryId)
    {
        if (slotIndex < 0)
        {
            throw new InvalidDataException(
                $"Inventory row {storageEntryId} has negative slot {slotIndex}.");
        }
    }

    private static void RequireUniqueSlots(
        IReadOnlyList<InventoryStorageItem> items, string source)
    {
        var seen = new HashSet<(InventoryContainerType ContainerType, int SlotIndex)>();
        foreach (var item in items)
        {
            if (!seen.Add((item.ContainerType, item.SlotIndex)))
            {
                throw new InvalidDataException(
                    $"The {source} contains duplicate slot {item.ContainerType}/{item.SlotIndex}.");
            }
        }
    }
}
