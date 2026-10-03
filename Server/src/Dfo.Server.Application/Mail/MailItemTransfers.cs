using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Application.Mail;

internal sealed record MailTakenItems(IReadOnlyList<ItemRecord> Items, IReadOnlyList<MailAssetChange> Changes);

/// <summary>Mail uses normal container references and detached records for every inventory mutation.</summary>
internal sealed class MailItemTransfers(IItemPlacementRuleService placements,
    ICharacterBusinessInventoryDomainService inventories, IItemArrivalRuleService arrivals,
    IItemLifecycleDomainService lifecycle, IMailItemRuleService sendRules, ICharacterTable owners)
{
    private static readonly BusinessInventorySpace[] CarriedSpaces =
    [BusinessInventorySpace.QuickSlot, BusinessInventorySpace.CarriedEquipment, BusinessInventorySpace.Consumable,
        BusinessInventorySpace.Material, BusinessInventorySpace.Quest, BusinessInventorySpace.ExpertMaterial,
        BusinessInventorySpace.AvatarEmblem, BusinessInventorySpace.Avatar, BusinessInventorySpace.CreatureInventory,
        BusinessInventorySpace.CreatureEquipment, BusinessInventorySpace.CreatureConsumable];

    public async ValueTask<ItemRecord> CreateStackAsync(int itemId, int count, CancellationToken ct)
    {
        var placement = await ValidateStackAsync(itemId, ct).ConfigureAwait(false);
        return await lifecycle.CreateAsync(new ItemSnapshot
        {
            ItemId = itemId, Count = count, IsPile = true, Type = placement.Traits, LegacyKind = placement.ItemKind,
            Marker16 = -1, RandomOptionChangedIndex = byte.MaxValue
        }, ct).ConfigureAwait(false);
    }

    private async ValueTask<ItemPlacement> ValidateStackAsync(int itemId, CancellationToken ct)
    {
        var placement = await placements.PlacementForAsync(itemId, ct).ConfigureAwait(false);
        if (placement is null || placement.ItemKind == 0 || placement.WorldClass != ItemWorldClass.Interchangeable)
            throw Error(MailFailure.InvalidItem, "Not a known stackable template.");
        return placement;
    }

    public async ValueTask<MailTakenItems> TakeEquipmentAsync(CharacterId character, ItemIdentity uid,
        bool sameAccount, CancellationToken ct)
    {
        foreach (var space in CarriedSpaces)
        {
            var container = inventories.GetStore(character, BusinessInventoryOwnership.StoreOf(space)).FindContainer(space);
            if (container is null || !container.StorageId.TryGetCharacterOwner(out var owner, out _) || owner != character) continue;
            foreach (var item in await container.GetAllAsync(ct).ConfigureAwait(false))
            {
                if (item.ItemUid != uid) continue;
                var placement = await placements.PlacementForAsync(item.ItemId, ct).ConfigureAwait(false);
                if (item.IsPile || placement is not { WorldClass: ItemWorldClass.Equipment }
                    || item.Type != placement.Traits || !(await sendRules.CanSendAsync(item.Snapshot, sameAccount, ct).ConfigureAwait(false)).Allowed)
                    throw Error(MailFailure.NotSendable, "Equipment cannot be sent.");
                var taken = await container.TakeAsync(item, null, ct).ConfigureAwait(false);
                return new([taken], [new(character, new(space, item.SlotIndex), item.ItemId, -1, null)]);
            }
        }
        throw Error(MailFailure.NotOwned, "Equipment is not in the sender's carried inventory.");
    }

    public async ValueTask<MailTakenItems> TakeStackAsync(CharacterId character, int itemId, int count,
        bool sameAccount, CancellationToken ct, BusinessSlot? sourceSlot = null)
    {
        var placement = await ValidateStackAsync(itemId, ct).ConfigureAwait(false);
        if (sourceSlot is { } selected && (!CarriedSpaces.Contains(selected.Space) || selected.Index < 0)
            || sourceSlot is not null && placement.Destination.Space == BusinessInventorySpace.SharedMaterial)
            throw Error(MailFailure.InvalidItem, "Invalid source slot.");
        var held = new List<(IBusinessItemContainer Container, ItemReference Item)>();
        foreach (var container in await ContainersAsync(character, placement, ct).ConfigureAwait(false))
            foreach (var item in await container.GetAllAsync(ct).ConfigureAwait(false))
                if ((sourceSlot is null || sourceSlot == new BusinessSlot(container.Space, item.SlotIndex))
                    && item.IsPile && item.ItemId == itemId
                    && (await sendRules.CanSendAsync(item.Snapshot, sameAccount, ct).ConfigureAwait(false)).Allowed)
                    held.Add((container, item));
        if (held.Sum(pair => (long)pair.Item.Count) < count)
            throw Error(MailFailure.InsufficientAssets, "Not enough sendable items.");
        var records = new List<ItemRecord>(); var changes = new List<MailAssetChange>();
        foreach (var (container, item) in held)
        {
            var amount = Math.Min(count, item.Count);
            records.Add(await container.TakeAsync(item, amount, ct).ConfigureAwait(false));
            changes.Add(new(character, new(container.Space, item.SlotIndex), itemId, -amount, null));
            count -= amount;
            if (count == 0) break;
        }
        return new(records, changes);
    }

    public async ValueTask<IReadOnlyList<MailAssetChange>> GrantAsync(CharacterId character,
        IReadOnlyList<ItemRecord> records, CancellationToken ct)
    {
        var changes = new List<MailAssetChange>();
        foreach (var record in records)
        {
            var placement = await placements.PlacementForAsync(record.ItemId, ct).ConfigureAwait(false);
            if (placement is null || placement.ItemKind == 0
                || record.IsPile != (placement.WorldClass == ItemWorldClass.Interchangeable)
                || !record.IsPile && placement.WorldClass != ItemWorldClass.Equipment)
                throw Error(MailFailure.InvalidItem, "Unsupported mailed item.");
            var containers = await ContainersAsync(character, placement, ct).ConfigureAwait(false);
            var target = containers.Single(value => value.Space == placement.Destination.Space);
            placement = InventoryCapacityPlacement.Limit(placement,
                await target.OpenSlotCountAsync(ct).ConfigureAwait(false));
            if (placement is null) throw Error(MailFailure.NoCapacity, "Inventory is full.");
            var openSlots = new Dictionary<BusinessInventorySpace, int>();
            foreach (var container in containers)
                openSlots.Add(container.Space, await container.OpenSlotCountAsync(ct).ConfigureAwait(false));
            ItemRecord? remaining = record;
            while (remaining is not null)
            {
                var held = new List<(IBusinessItemContainer Container, ItemReference Item)>();
                foreach (var container in containers)
                    held.AddRange((await container.GetAllAsync(ct).ConfigureAwait(false)).Select(item => (container, item)));
                var occupied = held.Select(pair => new BusinessSlot(pair.Container.Space, pair.Item.SlotIndex)).ToHashSet();
                if (openSlots.TryGetValue(BusinessInventorySpace.QuickSlot, out var quickOpen))
                    for (var index = Math.Max(0, quickOpen); index < 6; index++)
                        occupied.Add(new(BusinessInventorySpace.QuickSlot, index));
                var piles = held.Where(pair => pair.Item.SlotIndex < openSlots[pair.Container.Space]
                        && (pair.Container.Space != placement.Destination.Space
                            || placement.Destination.Contains(new(pair.Container.Space, pair.Item.SlotIndex)))
                        && pair.Item.Snapshot.CanStackWith(remaining.Snapshot))
                    .Select(pair => new ItemPile(new(pair.Container.Space, pair.Item.SlotIndex), pair.Item.Count)).ToArray();
                var arrival = arrivals.ForGrant(piles, remaining.Count, placement, occupied);
                var (slot, amount) = arrival switch
                {
                    ItemArrival.OntoExistingPile pile => (pile.Slot, pile.Added),
                    ItemArrival.IntoEmptySlot empty => (empty.Slot, empty.Count),
                    _ => throw Error(MailFailure.NoCapacity, "Inventory cannot hold the entire attachment.")
                };
                if (amount <= 0 || amount > remaining.Count) throw new InvalidDataException("Invalid item arrival quantity.");
                var receiving = containers.Single(value => value.Space == slot.Space);
                var portion = remaining;
                if (amount < remaining.Count)
                {
                    var split = await lifecycle.SplitAsync(remaining, amount, ct).ConfigureAwait(false);
                    portion = split.Taken; remaining = split.Remainder;
                }
                else remaining = null;
                await receiving.PutAsync(slot.Index, portion, ct).ConfigureAwait(false);
                changes.Add(new(character, slot, portion.ItemId, amount, null));
            }
        }
        return changes;
    }

    private async ValueTask<IReadOnlyList<IBusinessItemContainer>> ContainersAsync(CharacterId character,
        ItemPlacement placement, CancellationToken ct)
    {
        var account = await owners.FindOwnerAsync(character, ct).ConfigureAwait(false)
            ?? throw Error(MailFailure.CharacterNotFound, "Character was deleted.");
        var spaces = placement.Destination.Space != BusinessInventorySpace.SharedMaterial && ItemSlotRuleService.AllowsQuickSlot(placement)
            ? new[] { placement.Destination.Space, BusinessInventorySpace.QuickSlot } : [placement.Destination.Space];
        return spaces.Distinct().Select(space =>
        {
            var owner = space == BusinessInventorySpace.SharedMaterial
                ? new ContainerOwner(InventoryOwnerType.Account, account.Value)
                : new ContainerOwner(InventoryOwnerType.Character, character.Value);
            return inventories.GetStore(owner, BusinessInventoryOwnership.StoreOf(space)).GetContainer(space);
        }).ToArray();
    }
    private static MailException Error(MailFailure failure, string message) => new(failure, message);
}
