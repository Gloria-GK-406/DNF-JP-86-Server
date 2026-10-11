using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Application.Transactions;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemDeliveryDomainService(IItemPlacementRuleService placements,
    IItemArrivalRuleService arrivals, IItemLifecycleDomainService lifecycle,
    ICharacterBusinessInventoryDomainService inventories, ICharacterTable owners,
    IDomainTransactionAccessor transactions, IMailboxDomainService mailbox,
    IItemDeliveryRuleService rules) : IItemDeliveryDomainService
{
    public async ValueTask<long> PendingQuantityAsync(CharacterId character, int itemId, CancellationToken ct) =>
        (await mailbox.InboxAsync(character, ct).ConfigureAwait(false))
        .SelectMany(x => x.Attachments).Where(x => !x.Claimed)
        .Select(x => x.Asset).OfType<MailAsset.Stackable>().Where(x => x.ItemId == itemId)
        .Sum(x => (long)x.Count);

    public async ValueTask<ItemDeliveryReceipt> DeliverAsync(AccountId account, CharacterId character,
        IReadOnlyList<ItemDeliveryGrant> grants, string source, CancellationToken ct,
        ItemDeliveryMode mode = ItemDeliveryMode.InventoryThenMail)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("Item delivery requires the use-case transaction.");
        try
        {
            if (!Enum.IsDefined(mode) || string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Invalid delivery context.");
            if (await owners.FindOwnerAsync(character, ct).ConfigureAwait(false) != account)
                throw new InvalidOperationException("Item recipient is unavailable.");
            var added = new List<ItemDeliveryAddition>();
            var overflow = new List<ItemRecord>();
            foreach (var grant in grants)
            {
                if (grant.ItemId <= 0 || grant.Count <= 0 || grant.Instance is not null && grant.Snapshot is not null)
                    throw new ArgumentException("Invalid item grant.");
                var placement = await placements.PlacementForAsync(grant.ItemId, ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException($"Unknown reward {grant.ItemId}.");
                if (placement.WorldClass is not (ItemWorldClass.Interchangeable or ItemWorldClass.Equipment or ItemWorldClass.Avatar or ItemWorldClass.Creature)
                    || placement.Destination.Space is BusinessInventorySpace.VirtualResource or BusinessInventorySpace.TowerQuickSlot)
                    throw new InvalidDataException($"Reward {grant.ItemId} has no physical delivery route.");
                if (grant.PlacementConstraint is { } constraint)
                {
                    var band = constraint.Destination;
                    if (placement.WorldClass != ItemWorldClass.Interchangeable || band.Space != placement.Destination.Space
                        || band.FirstIndex < placement.Destination.FirstIndex || band.LastIndex > placement.Destination.LastIndex
                        || band.FirstIndex > band.LastIndex || constraint.StackLimit < 0)
                        throw new InvalidDataException("Reward placement may only restrict its catalog band.");
                    placement = placement with { Destination = band, StackLimit = Math.Min(
                        placement.StackLimit > 0 ? placement.StackLimit : int.MaxValue,
                        constraint.StackLimit > 0 ? constraint.StackLimit : int.MaxValue) };
                }
                var template = grant.Instance?.Snapshot ?? grant.Snapshot
                    ?? await rules.TemplateAsync(grant.ItemId, ct).ConfigureAwait(false);
                if (grant.Instance is not null && grant.Durability is { } changed && changed != template.Durability)
                    throw new InvalidDataException("An existing reward instance cannot be rewritten during delivery.");
                if (grant.Durability is { } durability) template = template with { Durability = durability };
                if (template.ItemId != grant.ItemId || template.LegacyKind != placement.ItemKind || template.Type != placement.Traits
                    || template.IsPile != (placement.WorldClass == ItemWorldClass.Interchangeable) || template.IsMirror)
                    throw new InvalidDataException("Reward instance does not match its template.");
                if (grant.Instance is not null && grant.Instance.Count != grant.Count)
                    throw new InvalidDataException("Reward instance quantity disagrees.");
                if (grant.Instance is not null && !template.IsPile && grant.Count != 1)
                    throw new InvalidDataException("An equipment instance cannot be duplicated.");
                var remaining = grant.Count;
                while (remaining > 0)
                {
                    var amount = template.IsPile ? remaining : 1;
                    var record = grant.Instance ?? await lifecycle.CreateAsync(template with
                    {
                        Count = amount, ItemUid = null,
                        InstanceValue = template.IsPile ? 0 : template.InstanceValue != 0 ? template.InstanceValue : EquipmentQualitySeeds.NewRandom(),
                    }, ct).ConfigureAwait(false);
                    remaining -= amount;
                    ItemRecord? pending = record;
                    while (pending is not null)
                    {
                        if (mode == ItemDeliveryMode.MailOnly) { overflow.Add(pending); break; }
                        var target = inventories.GetStore(character, BusinessInventoryOwnership.StoreOf(placement.Destination.Space))
                            .GetContainer(placement.Destination.Space, isDefault: true);
                        var destinations = new List<IBusinessItemContainer> { target };
                        if (grant.PlacementConstraint is null && placement.Destination.Space != BusinessInventorySpace.SharedMaterial && ItemSlotRuleService.AllowsQuickSlot(placement))
                            destinations.Add(inventories.GetStore(character, BusinessInventoryStoreKind.Item)
                                .GetContainer(BusinessInventorySpace.QuickSlot, isDefault: true));
                        var held = new List<(BusinessSlot Slot, ItemReference Item)>();
                        var occupied = new HashSet<BusinessSlot>();
                        foreach (var destination in destinations)
                        {
                            var open = await destination.OpenSlotCountAsync(ct).ConfigureAwait(false);
                            foreach (var item in await destination.GetAllAsync(ct).ConfigureAwait(false))
                            {
                                var slot = new BusinessSlot(destination.Space, item.SlotIndex);
                                occupied.Add(slot);
                                if (item.SlotIndex < open && (destination.Space != placement.Destination.Space
                                    || item.SlotIndex >= placement.Destination.FirstIndex && item.SlotIndex <= placement.Destination.LastIndex))
                                    held.Add((slot, item));
                            }
                            var end = destination.Space == placement.Destination.Space ? placement.Destination.LastIndex : 5;
                            for (var i = open; i <= end; i++) occupied.Add(new(destination.Space, i));
                        }
                        var piles = held.Where(x => x.Item.Snapshot.CanStackWith(pending.Snapshot))
                            .Select(x => new ItemPile(x.Slot, x.Item.Count)).ToArray();
                        // Shared materials have one bounded balance. Deliver its remaining capacity
                        // first; ordinary arrival keeps its all-or-nothing semantics for other callers.
                        var fitting = placement.Destination.Space == BusinessInventorySpace.SharedMaterial
                            ? (int)Math.Min(pending.Count, Math.Max(0, (placement.StackLimit > 0 ? placement.StackLimit : int.MaxValue)
                                - piles.Sum(x => (long)x.Count))) : pending.Count;
                        var arrival = fitting == 0 ? new ItemArrival.NoRoom()
                            : grant.PlacementConstraint is not null
                                ? arrivals.ForDailyRefill(piles, fitting, placement.Destination, occupied, placement.StackLimit)
                                : arrivals.ForGrant(piles, fitting, placement, occupied);
                        var next = arrival switch
                        {
                            ItemArrival.OntoExistingPile pile => (Slot: pile.Slot, Count: pile.Added),
                            ItemArrival.IntoEmptySlot empty => (Slot: empty.Slot, Count: empty.Count),
                            ItemArrival.NoRoom => ((BusinessSlot Slot, int Count)?)null,
                            _ => throw new InvalidDataException("Unsupported reward placement."),
                        };
                        if (next is null)
                        {
                            if (mode == ItemDeliveryMode.InventoryOnly) throw new ContainerAdditionException(ContainerAdditionFailure.NoRoom);
                            overflow.Add(pending); break;
                        }
                        if (next.Value.Count <= 0 || next.Value.Count > pending.Count) throw new InvalidDataException("Reward placement made no progress.");
                        var portion = pending;
                        if (next.Value.Count < pending.Count)
                        {
                            var split = await lifecycle.SplitAsync(pending, next.Value.Count, ct).ConfigureAwait(false);
                            portion = split.Taken; pending = split.Remainder;
                        }
                        else pending = null;
                        var bag = destinations.Single(x => x.Space == next.Value.Slot.Space);
                        var placed = await bag.PutAsync(next.Value.Slot.Index, portion, ct).ConfigureAwait(false);
                        await bag.FlushAsync(ct).ConfigureAwait(false);
                        added.Add(new(bag, next.Value.Slot, next.Value.Count, placed.Snapshot));
                    }
                }
            }
            var ids = new List<long>();
            foreach (var batch in overflow.Chunk(10))
            {
                var builder = MailBuilder.FromSystem(character).WithText(source, mode == ItemDeliveryMode.MailOnly
                    ? "奖励已通过邮件发放。" : "背包空间不足，剩余奖励已通过邮件发放。");
                var attachments = new List<PreparedMailAttachment>();
                foreach (var item in batch)
                {
                    MailAsset asset;
                    if (item.IsPile) { builder.AddStackable(item.ItemId, item.Count); asset = new MailAsset.Stackable(item.ItemId, item.Count); }
                    else { var uid = item.ItemUid ?? throw new InvalidDataException("Reward instance has no identity."); builder.AddEquipment(uid); asset = new MailAsset.Equipment(uid); }
                    attachments.Add(new(asset, [item]));
                }
                ids.Add((await mailbox.SendPreparedAsync(builder.Build(), attachments, ct).ConfigureAwait(false)).MailId);
            }
            return new(added, ids, overflow.Sum(x => (long)x.Count)) { MailedItems = overflow.Select(x => x.Snapshot.DeepCopy()).ToArray() };
        }
        catch
        {
            if (transaction is not DomainTransaction { IsSettled: true }) transaction.RequireRollback();
            throw;
        }
    }
}
