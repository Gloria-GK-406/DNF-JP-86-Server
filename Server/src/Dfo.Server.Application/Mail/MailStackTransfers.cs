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
    IItemPlacementRuleService placements, ICharacterContainerDomainService containers,
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

    public async ValueTask<IReadOnlyList<MailAssetChange>> TakeAsync(CharacterId character, int itemId, int count, bool sameAccount, CancellationToken ct, GameSlot? sourceSlot = null)
    {
        var placement = await ValidateAsync(itemId, ct);
        if (sourceSlot is { } selected && (!Carried.Contains(selected.Space) || selected.Index < 0
            || selected.Space == InventoryContainerType.Main && selected.Index < 3))
            throw new MailException(MailFailure.InvalidItem, "Invalid source slot.");
        if (accountRules.TryGetByItemId(itemId, out var accountMaterial))
        {
            if (sourceSlot is not null) throw new MailException(MailFailure.InvalidItem, "Account materials cannot be selected as physical mail attachments.");
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
                if ((sourceSlot is null || sourceSlot.Value.Space == type && sourceSlot.Value.Index == item.SlotIndex) && item.Core.ItemId == itemId && (await sendRules.CanSendAsync(item.Core, sameAccount, ct)).Allowed)
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
        await ValidateAsync(itemId, ct);
        try
        {
            var added = await containers.AddAsync(character, itemId, count, ct);
            if (added.AccountMaterial is { } material)
                return [new MailAssetChange(character, null, null, itemId, count, null) { AccountMaterial = material }];
            return added.Items.Select(x => new MailAssetChange(character, x.Type, x.SlotIndex, itemId, x.Count, null) { ContainerId = x.Container.Value }).ToArray();
        }
        catch (ContainerAdditionException e)
        {
            throw new MailException(e.Reason switch
            {
                ContainerAdditionFailure.NoRoom => MailFailure.NoCapacity,
                ContainerAdditionFailure.CharacterNotFound => MailFailure.CharacterNotFound,
                _ => MailFailure.InvalidItem
            }, e.Message);
        }
    }
}
