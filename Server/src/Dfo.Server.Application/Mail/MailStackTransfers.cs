using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Application.Mail;

internal sealed class MailStackTransfers(IItemDomainStore items, IDomainTransactionAccessor transactions,
    IItemPlacementRuleService placements, IItemArrivalRuleService arrivals, IInventorySlotMappingRuleService mapping,
    IMailItemRuleService sendRules, IAccountMaterialRuleService accountRules,
    IAccountMaterialBagDomainService accountBags, ICharacterTable owners)
{
    private static readonly InventoryContainerType[] Carried = [InventoryContainerType.Main, InventoryContainerType.Pet];
    private IItemDomainStoreSession Session => items.SessionIn(transactions.Current ?? throw new InvalidOperationException("Mail assets require a transaction."));

    public async ValueTask<ItemPlacement> ValidateAsync(int itemId, CancellationToken ct)
    {
        var placement = await placements.PlacementForAsync(itemId, ct);
        if (placement is null || placement.ItemKind == 0 || await placements.ClassifyAsync(itemId, ct) != ItemWorldClass.Interchangeable)
            throw new MailException(MailFailure.InvalidItem, $"{itemId} is not a known stackable template.");
        return placement;
    }

    public async ValueTask<IReadOnlyList<MailAssetChange>> TakeAsync(CharacterId character, int itemId, int count, bool sameAccount, CancellationToken ct)
    {
        var placement = await ValidateAsync(itemId, ct);
        if (accountRules.TryGetByItemId(itemId, out var accountMaterial))
        {
            if (!(await sendRules.CanSendAsync(StackableItemCore.Create(placement.ItemKind, itemId, count), sameAccount, ct)).Allowed)
                throw new MailException(MailFailure.NotSendable, "Account material is not sendable.");
            var account = await owners.FindOwnerAsync(character, ct) ?? throw new MailException(MailFailure.CharacterNotFound, "Character was deleted.");
            var change = await accountBags.Get(account).ConsumeAsync(accountMaterial.Kind, count, ct)
                ?? throw new MailException(MailFailure.InsufficientAssets, "Not enough account materials.");
            return [new MailAssetChange(character, null, null, itemId, -count, null) { AccountMaterial = change }];
        }
        var session = Session;
        var held = new List<(ContainerId Container, InventoryContainerType Type, StoredItem Item)>();
        foreach (var type in Carried)
        {
            var container = ContainerId.ForCharacter(character, type);
            foreach (var item in await session.ReadAllAsync(container, ct))
                if (item.Core.ItemId == itemId && (await sendRules.CanSendAsync(item.Core, sameAccount, ct)).Allowed)
                    held.Add((container, type, item));
        }
        if (held.Sum(x => (long)x.Item.Core.Value) < count)
            throw new MailException(MailFailure.InsufficientAssets, "Not enough sendable items.");
        var changes = new List<MailAssetChange>();
        foreach (var (container, type, item) in held)
        {
            var taken = Math.Min(count, item.Core.Value);
            if (taken == item.Core.Value) session.Clear(container, item.SlotIndex);
            else session.Write(container, item with { Core = item.Core with { Value = item.Core.Value - taken }, WorldClass = ItemWorldClass.Interchangeable });
            changes.Add(new(character, type, item.SlotIndex, itemId, -taken, null));
            count -= taken;
            if (count == 0) break;
        }
        return changes;
    }

    public async ValueTask<IReadOnlyList<MailAssetChange>> GrantAsync(CharacterId character, int itemId, int count, CancellationToken ct)
    {
        var placement = await ValidateAsync(itemId, ct);
        if (accountRules.TryGetByItemId(itemId, out var accountMaterial))
        {
            var account = await owners.FindOwnerAsync(character, ct) ?? throw new MailException(MailFailure.CharacterNotFound, "Character was deleted.");
            var accountChanges = await accountBags.Get(account).AddAsync(new Dictionary<AccountMaterialKind, int> { [accountMaterial.Kind] = count }, ct);
            return [new MailAssetChange(character, null, null, itemId, count, null) { AccountMaterial = accountChanges.Single() }];
        }
        var session = Session;
        var changes = new List<MailAssetChange>();
        while (count > 0)
        {
            var held = new Dictionary<BusinessSlot, StoredItem>();
            foreach (var type in Carried)
                foreach (var item in await session.ReadAllAsync(ContainerId.ForCharacter(character, type), ct))
                    held.Add(mapping.ToBusinessSlot(new GameSlot(type, item.SlotIndex)), item);
            var arrival = arrivals.ForGrant(held.Where(x => x.Value.Core.ItemId == itemId)
                .Select(x => new ItemPile(x.Key, x.Value.Core.Value)).ToArray(), count, placement, held.Keys.ToHashSet());
            BusinessSlot slot;
            int granted;
            InventoryItemCoreState core;
            switch (arrival)
            {
                case ItemArrival.OntoExistingPile onto:
                    slot = onto.Slot; granted = onto.Added; core = held[slot].Core with { Value = onto.Total }; break;
                case ItemArrival.IntoEmptySlot empty:
                    slot = empty.Slot; granted = empty.Count; core = StackableItemCore.Create(placement.ItemKind, itemId, granted); break;
                default: throw new MailException(MailFailure.NoCapacity, "The complete attachment does not fit in the inventory.");
            }
            var target = mapping.ToGameSlot(slot);
            if (!Carried.Contains(target.Space) || granted <= 0 || granted > count)
                throw new MailException(MailFailure.InvalidItem, "Unsupported mail item destination.");
            session.Write(ContainerId.ForCharacter(character, target.Space), new StoredItem(target.Index, core) { WorldClass = ItemWorldClass.Interchangeable });
            changes.Add(new(character, target.Space, target.Index, itemId, granted, null));
            count -= granted;
        }
        return changes;
    }
}
