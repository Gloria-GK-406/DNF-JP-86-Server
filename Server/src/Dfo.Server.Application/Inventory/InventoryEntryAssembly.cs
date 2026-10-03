using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
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
/// Nine tables between the two loads, and what makes them one inventory is not in any of them:
/// which containers may appear where, that an avatar item without its facts is not an avatar while
/// one in the account cargo never had any, that the locks table is read whole by both loads and then
/// divided between them, and that the cooldowns and the effects arrive as one list in that order.
/// Every one of those is a decision about inventories, so all of them are here; each table only
/// knows how to read its own rows.
///
/// The refusals are the load's, not the tables'. A row that names the wrong owner is a row the
/// table can read perfectly well -- whether it may be used is this one's question.
/// </remarks>
internal sealed class InventoryEntryAssembly(
    ICharacterTable characters,
    ICharacterBusinessInventoryDomainService inventories,
    ICharacterItemTable characterItems,
    IAccountCargoItemTable cargoItems,
    ICharacterContainerStateTable containerStates,
    IAccountCargoStateTable cargoStates,
    ICharacterGoldLimitTable goldLimits,
    ICharacterCarriedGoldTable carriedGold,
    ICharacterItemCooldownTable itemCooldowns,
    ICharacterItemEffectTable itemEffects,
    ICharacterItemLockTable itemLocks,
    ICharacterRentalTable rentals,
    IItemDomainStore itemStore, IDomainTransactionAccessor transactions)
    : ICharacterInventoryEntryRepository, IAccountInventoryEntryRepository
{
    /// <summary>Where the gold a character carries is drawn, and the id it is drawn under.</summary>
    /// <remarks>
    /// The client's own convention: the main list's first virtual slot, item id zero. The same
    /// zero a floor drop uses to mean "this pile is gold".
    /// </remarks>
    private const int CarriedGoldSlot = 0;

    private const int CarriedGoldItemId = 0;

    public async ValueTask<CharacterInventoryEntryStorageRecord> LoadCharacterAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        ValidateIdentifiers(accountId, characterId);
        await RequireOwnershipAsync(accountId, characterId, cancellationToken).ConfigureAwait(false);

        var items = await ReadCharacterItemsAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var containerParameters = await ReadContainerParametersAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var virtualResources = await this
            .CharacterVirtualResourcesAsync(characterId, items, cancellationToken)
            .ConfigureAwait(false);
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

    /// <remarks>
    /// The character a row belongs to used to be stored twice -- once as the owner and once beside
    /// it -- and the two were checked against each other. Only the owner is stored now, so the
    /// character the item names is the owner, and the disagreement the check refused has nowhere
    /// left to happen.
    /// </remarks>
    private async ValueTask<IReadOnlyList<ItemReference>> ReadCharacterItemsAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var items = new List<ItemReference>();
        var rows = await characterItems.ReadByCharacterAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        RequireOneNameEach(rows.Select(row => row.ItemUid), "character inventory");
        foreach (var row in rows)
        {
            var ownerType = ParseOwnerType(row.OwnerScope);
            var containerType = ParseContainerType(row.ListType, allowAccountCargo: false);
            if (ownerType != InventoryOwnerType.Character || row.OwnerId != characterId.Value)
            {
                throw new InvalidDataException(
                    $"Inventory row {row.ItemUid} has invalid character ownership.");
            }

            RequireSlot(row.SlotIndex, row.ItemUid);
            items.Add(await ReadItemAsync(ContainerId.ForCharacter(characterId, containerType), row.SlotIndex,
                cancellationToken).ConfigureAwait(false));
        }

        return items;
    }

    private async ValueTask<IReadOnlyList<ItemReference>> ReadCargoItemsAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        var items = new List<ItemReference>();
        var rows = await cargoItems.ReadByAccountAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        RequireOneNameEach(rows.Select(row => row.ItemUid), "account cargo");
        foreach (var row in rows)
        {
            var containerType = ParseContainerType(row.ListType, allowAccountCargo: true);
            if (row.AccountId != accountId.Value
                || containerType != InventoryContainerType.AccountCargo)
            {
                throw new InvalidDataException(
                    $"Inventory row {row.ItemUid} has invalid account cargo ownership.");
            }

            RequireSlot(row.SlotIndex, row.ItemUid);
            items.Add(await ReadItemAsync(ContainerId.ForAccount(accountId, containerType), row.SlotIndex,
                cancellationToken).ConfigureAwait(false));
        }

        return items;
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
    /// Slot 0 is the gold the character carries, slot 1 the revival coin, slot 2 the PvP point --
    /// see <see cref="InventoryRuntimeValueState"/>.
    /// <para>
    /// <b>Slot 0 is read from the character rather than from an item row.</b> The client draws it
    /// as an item because its inventory window renders a currency with the widget it renders a
    /// sword with; that is an encoding, and this is where the encoding is applied. What gold
    /// actually is, is a figure on the character, and it is stored as one so that a change to it
    /// can be a single statement.
    /// </para>
    /// <para>
    /// Slots 1 and 2 come out of the items already loaded. Character creation grants ten revival
    /// coins in slot 1, and package grants can increase that balance. These counters are projected
    /// as virtual resources rather than ordinary carried items.
    /// </para>
    /// </remarks>
    private async ValueTask<IReadOnlyList<InventoryRuntimeValueState>> CharacterVirtualResourcesAsync(
        CharacterId characterId,
        IReadOnlyList<ItemReference> items,
        CancellationToken cancellationToken)
    {
        var carried = await carriedGold.FindAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var values = new List<InventoryRuntimeValueState>();

        // Only for a character this server actually has. A figure on one it does not would be
        // invented rather than read, and an empty load has to stay empty.
        if (carried is not null)
            values.Add(new(CarriedGoldSlot, CarriedGoldItemId, Math.Max(0, carried.Carried)));

        values.AddRange(items
            .Where(item => StorageType(item) == InventoryContainerType.Main
                && item.SlotIndex is > CarriedGoldSlot and <= 2)
            .Select(item => new InventoryRuntimeValueState(
                item.SlotIndex, item.ItemId, Math.Max(0, (item.IsPile ? item.Count : item.Snapshot.InstanceValue)))));
        values.Sort((left, right) => left.SlotIndex.CompareTo(right.SlotIndex));
        return values;
    }

    private async ValueTask<IReadOnlyList<InventoryRuntimeValueState>> AccountVirtualResourcesAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        var container = inventories.GetStore(new ContainerOwner(InventoryOwnerType.Account, accountId.Value),
            BusinessInventoryStoreKind.Item).GetContainer(BusinessInventorySpace.SharedMaterial);
        var contents = await container.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var counts = contents.ToDictionary(item => item.SlotIndex, item => item.Count);
        return AccountMaterialCatalog.Definitions.Select((material, index) => new InventoryRuntimeValueState(
                material.VirtualSlotIndex, material.ItemId, counts.GetValueOrDefault(index)))
            .Where(value => value.Value > 0).ToArray();
    }

    /// <summary>The cooldowns and then the effects, which is the order the client expects.</summary>
    /// <remarks>
    /// One table used to hold both, with a column saying which a row was and the value meaning one
    /// thing or the other accordingly. Two tables say it now, and the order that used to come out
    /// of that column comes out of the order they are read in.
    /// </remarks>
    private async ValueTask<IReadOnlyList<InventoryItemValueStorageRecord>> ReadItemValuesAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var values = new List<InventoryItemValueStorageRecord>();
        var seen = new HashSet<(InventoryItemValueListType ListType, int SortOrder)>();
        foreach (var (listType, rows) in new[]
                 {
                     (InventoryItemValueListType.Cooldown,
                         await itemCooldowns.ReadByCharacterAsync(characterId, cancellationToken)
                             .ConfigureAwait(false)),
                     (InventoryItemValueListType.Effect,
                         await itemEffects.ReadByCharacterAsync(characterId, cancellationToken)
                             .ConfigureAwait(false)),
                 })
        {
            foreach (var row in rows)
            {
                var storedCharacterId = new CharacterId(row.CharacterId);
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
    /// What the character has rented.
    /// </summary>
    /// <remarks>
    /// This used to read two tables and check for duplicates across both, because the same rental
    /// could be written to either. One table holds them now, and the duplicate check it needed is
    /// the key that table declares -- but the check stays, because it also refuses a second rental
    /// sharing an inventory template, which no key expresses.
    /// </remarks>
    private async ValueTask<IReadOnlyList<CharacterRentalStorageRecord>> ReadRentalsAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var accepted = new List<CharacterRentalStorageRecord>();
        var itemIds = new HashSet<long>();
        var templateIds = new HashSet<long>();
        foreach (var row in await rentals.ReadByCharacterAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
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

    private async ValueTask<ItemReference> ReadItemAsync(ContainerId container, int slot, CancellationToken ct) =>
        await new AmbientStoreAccess(transactions).ReadAsync(itemStore, session => session.GetAsync(container, slot, ct)).ConfigureAwait(false)
        ?? throw new InvalidDataException("Inventory row has no corresponding item occupancy.");

    private static InventoryContainerType StorageType(ItemReference item) =>
        item.ContainerId.TryGetOwner(out _, out var type) ? type
            : throw new InvalidDataException("Inventory item has an invalid container.");

    private static void RequireOneNameEach(IEnumerable<long> namedRows, string source)
    {
        var seen = new HashSet<long>();
        foreach (var itemUid in namedRows)
        {
            if (!seen.Add(itemUid))
            {
                throw new InvalidDataException(
                    $"The {source} names item {itemUid} in more than one position.");
            }
        }
    }

    private static void RequireUniqueSlots(
        IReadOnlyList<ItemReference> items, string source)
    {
        var seen = new HashSet<(InventoryContainerType ContainerType, int SlotIndex)>();
        foreach (var item in items)
        {
            if (!seen.Add((StorageType(item), item.SlotIndex)))
            {
                throw new InvalidDataException(
                    $"The {source} contains duplicate slot {StorageType(item)}/{item.SlotIndex}.");
            }
        }
    }
}
