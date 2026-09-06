using Dfo.Server.Abstractions.Content;
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
    IAccountTable accounts,
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
    IItemPlacementRuleService placementRules)
    : ICharacterInventoryEntryRepository, IAccountInventoryEntryRepository,
        IEntryInventoryLeasePersistence
{
    /// <summary>Where the gold a character carries is drawn, and the id it is drawn under.</summary>
    /// <remarks>
    /// The client's own convention: the main list's first virtual slot, item id zero. The same
    /// zero a floor drop uses to mean "this pile is gold".
    /// </remarks>
    private const int CarriedGoldSlot = 0;

    private const int CarriedGoldItemId = 0;

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

        // What the scan keeps is exactly what has a name. A thing with a name is the same thing
        // after this save as before it, so its row is found again by that name and kept; a pile
        // has no name because two of it are the same thing, so there is nothing about the row it
        // sat on worth keeping. The piles this save is writing are therefore taken away here and
        // written again below, within the same save -- which is all a pile has ever been, a count
        // in a position. Keeping them by a row key instead would be handing a pile the very mark
        // that would let somebody tell two identical piles apart.
        var present = lease.Inventory.Containers
            .Where(value => value.ContainerType != InventoryContainerType.AccountCargo)
            .SelectMany(value => value.Items)
            .Select(value => value.Identity)
            .OfType<ItemIdentity>()
            .Select(identity => (long)identity.Value)
            .Distinct().ToArray();
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

        // What each thing is, settled first and all of it, while the positions the load read are
        // still standing. Those positions are what says a thing is this character's, so an update
        // that ran after they were taken away would have nothing left to check itself against.
        var settled = new List<SettledItem>();
        foreach (var container in lease.Inventory.Containers.Where(
                     value => value.ContainerType != InventoryContainerType.AccountCargo))
        {
            foreach (var item in container.Items)
            {
                // Classified before it is written, out of the one place that classifies. The
                // archive is what says a template is an avatar; a row it declares one that is
                // being written as something else is the silent disagreement this classification
                // exists to make audible, and it is refused rather than stored.
                var worldClass = await placementRules
                    .ClassifyAsync(item.Core.ItemId, cancellationToken).ConfigureAwait(false);
                if (worldClass == ItemWorldClass.Avatar && item.Core.ItemKind != 8)
                {
                    throw new InvalidDataException(
                        $"The archive declares item {item.Core.ItemId} an avatar, but the row in "
                        + $"slot {item.SlotIndex} carries kind {item.Core.ItemKind}.");
                }

                // The avatar facts go in with the row rather than after it. An item that is not
                // an avatar sends none, which clears whatever the row held -- an item that stopped
                // being an avatar and kept them would be refused by the encoder later on.
                var avatarDetail = item.Core.ItemKind == 8 ? item.AvatarDetail : null;
                long? identity;
                if (item.Identity is { } named)
                {
                    identity = await characterItems.UpdateOwnAsync(
                        named.Value, characterId, item.Core, avatarDetail, cancellationToken)
                        .ConfigureAwait(false) == 1
                        ? named.Value
                        : null;
                }
                else
                {
                    identity = await characterItems.InsertOwnAsync(
                        item.Core, avatarDetail, cancellationToken).ConfigureAwait(false);
                }

                if (identity is not { } written)
                {
                    // An insert that lands on nothing is an avatar UID another row already holds;
                    // there is no other condition an insert here can fail to meet. An update that
                    // lands on nothing is that, or a row that is no longer the character's.
                    throw new InvalidDataException(item.Identity is { } missing
                        ? $"Dirty inventory item {missing.Value} is stale or missing."
                        : $"Dirty inventory item in slot {item.SlotIndex} was not persisted.");
                }

                settled.Add(new SettledItem(
                    (int)container.ContainerType,
                    item.SlotIndex,
                    worldClass,
                    checked((int)written),
                    item.Core.ItemId,
                    item.Core.Value));
            }
        }

        // One thing, one position. A lease naming the same thing twice used to cost the player
        // that thing's first position and nothing else, because a row could only be in one place;
        // now that a position names a row rather than holding it, two of them would name the same
        // row and the player would appear to have two of something there is one of -- and could
        // then move both away. It is refused before anything is written.
        var claimed = settled.Select(item => item.Identity).ToHashSet();
        if (claimed.Count != settled.Count)
        {
            throw new InvalidDataException(
                "A dirty inventory names the same thing in more than one position.");
        }

        // Every position at once, and only then where each thing ended up. Emptying them one by
        // one as each thing was written would put a thing down in a place another has not left
        // yet, which is exactly the collision the old shape needed somewhere to park to avoid.
        await characterItems.ClearOwnPositionsAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        foreach (var item in settled)
        {
            var pile = item.WorldClass is ItemWorldClass.Interchangeable;
            if (await characterItems.HoldOwnAsync(
                    characterId,
                    item.ListType,
                    item.SlotIndex,
                    pile ? ItemSlotKind.Template : ItemSlotKind.World,
                    pile ? item.TemplateId : item.Identity,
                    item.Identity,
                    pile ? Math.Max(item.Held, 1) : 1,
                    cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidDataException(
                    $"Two of this character's things claim container {item.ListType} "
                    + $"slot {item.SlotIndex}.");
            }
        }
    }

    /// <summary>One thing this save has settled, and where the lease says it belongs.</summary>
    /// <param name="Identity">The row it was written to, which a position is about to name.</param>
    /// <param name="TemplateId">What the archive calls it, which a pile's position names instead.</param>
    /// <param name="Held">What its core says it holds, which is a pile's count and nothing else's.</param>
    private sealed record SettledItem(
        int ListType,
        int SlotIndex,
        ItemWorldClass WorldClass,
        int Identity,
        int TemplateId,
        int Held);

    /// <summary>The name of the thing on this row, or nothing when the row holds a pile.</summary>
    /// <remarks>
    /// Asked of the archive out of the one place that classifies, not read off the kind byte the
    /// row carries -- a row is written by callers free to put anything in that byte, which is why
    /// the classification exists at all.
    ///    ///
    /// A pile is left unnamed deliberately. Its row key would be a perfectly good number and a
    /// completely wrong fact: it names the row, not the thing, and a pile is the one kind of
    /// content for which no such thing exists to be named.
    /// </remarks>
    private async ValueTask<ItemIdentity?> IdentityOfAsync(
        long itemUid, InventoryItemCoreState core, CancellationToken cancellationToken)
    {
        var worldClass = await placementRules.ClassifyAsync(core.ItemId, cancellationToken)
            .ConfigureAwait(false);
        return worldClass is ItemWorldClass.Interchangeable
            ? null
            : new ItemIdentity(checked((int)itemUid));
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
    private async ValueTask<IReadOnlyList<InventoryStorageItem>> ReadCharacterItemsAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var items = new List<InventoryStorageItem>();
        foreach (var row in await characterItems.ReadByCharacterAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            var ownerType = ParseOwnerType(row.OwnerScope);
            var containerType = ParseContainerType(row.ListType, allowAccountCargo: false);
            if (ownerType != InventoryOwnerType.Character || row.OwnerId != characterId.Value)
            {
                throw new InvalidDataException(
                    $"Inventory row {row.ItemUid} has invalid character ownership.");
            }

            RequireSlot(row.SlotIndex, row.ItemUid);
            items.Add(WithAvatarDetail(new InventoryStorageItem(
                await IdentityOfAsync(row.ItemUid, row.Core, cancellationToken)
                    .ConfigureAwait(false),
                ownerType, row.OwnerId, characterId, containerType,
                row.SlotIndex, row.Core), row.ItemUid, row.Avatar));
        }

        return items;
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
                await IdentityOfAsync(row.ItemUid, row.Core, cancellationToken)
                    .ConfigureAwait(false),
                InventoryOwnerType.Account, row.AccountId, storedCharacterId,
                containerType, row.SlotIndex, row.Core));
        }

        // No avatar facts here, unlike the character's own items. They were only ever a
        // character's, so a cargo row has never carried any: an avatar sitting in the cargo is
        // carried with none at all, and that is not an error the way it would be for a character.
        return items;
    }

    /// <summary>An avatar item is not one until the facts on its row are found and are usable.</summary>
    /// <remarks>
    /// Three rules meet here, and none of them can be written as a constraint because each turns on
    /// a byte inside the core. An item is an avatar when its kind is 8; a character's avatar must
    /// name a usable UID and must carry its facts; and an item that is not an avatar must carry
    /// none, which the encoder refuses rather than ignores.
    ///
    /// The checks that survive are about the values. The three that compared this row against a
    /// second one -- the owner, the character and the template id -- are gone with the second row.
    /// </remarks>
    private static InventoryStorageItem WithAvatarDetail(
        InventoryStorageItem item, long itemUid, AvatarDetailRow? detail)
    {
        if (item.Core.ItemKind != 8)
        {
            return item;
        }

        if (item.Core.Value <= 0)
        {
            throw new InvalidDataException(
                $"Avatar inventory row {itemUid} has no avatar UID.");
        }

        if (detail is null)
        {
            throw new InvalidDataException($"Avatar detail {item.Core.Value} is unavailable.");
        }

        if (detail.JewelSocket.Length != 30
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
    /// Slots 1 and 2 still come out of the items already loaded. Nothing writes them yet either,
    /// so they are empty today; when a revival coin or a PvP point becomes real it follows slot 0
    /// out of the item rows rather than the other way round -- they are three currencies, and none
    /// of the three is an item.
    /// </para>
    /// </remarks>
    private async ValueTask<IReadOnlyList<InventoryRuntimeValueState>> CharacterVirtualResourcesAsync(
        CharacterId characterId,
        IReadOnlyList<InventoryStorageItem> items,
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
            .Where(item => item.ContainerType == InventoryContainerType.Main
                && item.SlotIndex is > CarriedGoldSlot and <= 2)
            .Select(item => new InventoryRuntimeValueState(
                item.SlotIndex, item.Core.ItemId, Math.Max(0, item.Core.Value))));
        values.Sort((left, right) => left.SlotIndex.CompareTo(right.SlotIndex));
        return values;
    }

    private async ValueTask<IReadOnlyList<InventoryRuntimeValueState>> AccountVirtualResourcesAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        var row = await accounts.FindCubeFragmentsAsync(accountId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidDataException(
                $"Account {accountId.Value} is missing while loading inventory values.");
        int[] counts =
            [row.BlackCube, row.WhiteCube, row.RedCube, row.BlueCube, row.ClearCube, row.GoldCube];
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
