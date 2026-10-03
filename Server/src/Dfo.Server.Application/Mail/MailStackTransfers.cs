using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Application.Mail;

internal sealed class MailStackTransfers(IItemPlacementRuleService placements, ICharacterContainerDomainService containers,
    ICharacterBusinessInventoryDomainService inventories,
    IMailItemRuleService sendRules, ICharacterTable owners)
{
    public async ValueTask<ItemPlacement> ValidateAsync(int itemId, CancellationToken ct)
    {
        var placement = await placements.PlacementForAsync(itemId, ct);
        if (placement is null || placement.ItemKind == 0 || await placements.ClassifyAsync(itemId, ct) != ItemWorldClass.Interchangeable)
            throw new MailException(MailFailure.InvalidItem, $"{itemId} is not a known stackable template.");
        return placement;
    }

    public async ValueTask<IReadOnlyList<MailAssetChange>> TakeAsync(CharacterId character, int itemId, int count, bool sameAccount, CancellationToken ct, BusinessSlot? sourceSlot = null)
    {
        var placement = await ValidateAsync(itemId, ct);
        if (sourceSlot is { } selected && (!CanCarry(selected.Space) || selected.Index < 0))
            throw new MailException(MailFailure.InvalidItem, "Invalid source slot.");
        if (sourceSlot is not null && placement.Destination.Space == BusinessInventorySpace.SharedMaterial)
            throw new MailException(MailFailure.InvalidItem, "Shared materials cannot be selected as explicit mail attachment slots.");
        var account = await owners.FindOwnerAsync(character, ct)
            ?? throw new MailException(MailFailure.CharacterNotFound, "Character was deleted.");
        var held = new List<(IBusinessItemContainer Container, BusinessSlot Slot, ContainerSlotItem Item)>();
        var spaces = placement.Destination.Space != BusinessInventorySpace.SharedMaterial && ItemSlotRuleService.AllowsQuickSlot(placement)
            ? new[] { placement.Destination.Space, BusinessInventorySpace.QuickSlot }
            : [placement.Destination.Space];
        foreach (var space in spaces.Distinct())
        {
            if (!CanCarry(space) && space != BusinessInventorySpace.SharedMaterial) continue;
            var owner = space == BusinessInventorySpace.SharedMaterial
                ? new ContainerOwner(InventoryOwnerType.Account, account.Value)
                : new ContainerOwner(InventoryOwnerType.Character, character.Value);
            var container = inventories.GetStore(owner, BusinessInventoryOwnership.StoreOf(space))
                .FindContainer(space);
            if (container is null) continue;
            foreach (var item in await container.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var slot = new BusinessSlot(space, item.SlotIndex);
                if ((sourceSlot is null || sourceSlot == slot) && item.ItemId == itemId
                    && (await sendRules.CanSendAsync(item.Core, sameAccount, ct)).Allowed)
                    held.Add((container, slot, item));
            }
        }
        if (held.Sum(x => (long)x.Item.Count) < count)
            throw new MailException(MailFailure.InsufficientAssets, "Not enough sendable items.");
        var changes = new List<MailAssetChange>();
        foreach (var (container, slot, item) in held)
        {
            var taken = Math.Min(count, item.Count);
            if (taken == item.Count) await container.ClearAsync(slot.Index, ct).ConfigureAwait(false);
            else await container.PutAsync(slot.Index, item.Core with { Value = item.Count - taken }, ct).ConfigureAwait(false);
            changes.Add(new(character, slot, itemId, -taken, null));
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
            return added.Items.Select(x => new MailAssetChange(character, x.Slot, itemId, x.Count, null)).ToArray();
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

    private static bool CanCarry(BusinessInventorySpace space) => space.IsCarriedItemSpace()
        || space is BusinessInventorySpace.Avatar or BusinessInventorySpace.CreatureInventory
            or BusinessInventorySpace.CreatureEquipment or BusinessInventorySpace.CreatureConsumable;
}
