using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class SelectCharacterEntryInventoryDomainService(
    IInventoryLifecycleRuleService lifecycleRules,
    IEntryInventoryMutationRepository mutations,
    ICharacterInventoryEntryDomainService inventories,
    ICharacterContainerDomainService containers,
    IItemDomainStore itemStore,
    IDomainTransactionAccessor transactions,
    IItemPlacementRuleService placementRules,
    IItemArrivalRuleService arrivals,
    IItemSlotRuleService slotRules,
    IInventorySlotMappingRuleService? slotMappings = null) : ISelectCharacterEntryInventoryDomainService
{
    public async ValueTask<SelectCharacterResult> PrepareLeaseAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken)
    {
        var reloaded = await inventories.LoadAsync(
            session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        BindContainers(session.CharacterId, reloaded);
        return result with { CharacterInventory = reloaded };
    }

    /// <summary>
    /// Gives the character the container its items already live in.
    /// </summary>
    /// <remarks>
    /// Entry is where a character acquires the container it falls back to, so everything registered
    /// afterwards -- a dungeon's own inventory, and whatever comes after that -- has somewhere to
    /// return to. Rebinding from scratch keeps a re-entry from inheriting the previous one's
    /// registrations.
    ///
    /// A loaded inventory with no main container is refused rather than passed over. Entry is the
    /// only place the binding is made, and the entry snapshot does not come from the container, so
    /// a character that got past here unbound would be shown a full inventory whose every drag came
    /// back refused -- with an error code a dozen legitimate refusals also use.
    /// </remarks>
    private void BindContainers(CharacterId characterId, CharacterInventoryEntryState inventory)
    {
        var main = inventory.Containers.SingleOrDefault(
                value => value.ContainerType == InventoryContainerType.Main)
            ?? throw new InvalidDataException(
                "Entry inventory has no main container to bind the character to.");

        // The equipment container is bound alongside it. Wearing a piece is a move between the two
        // lists, so both ends have to be containers the same way -- and the character's equipment
        // rows already exist whether or not anything is bound to them, which is what made the
        // player's drag from the bag onto the shoulder slot come back refused for a list this
        // server "does not serve" (2026-08-26).
        var equipment = inventory.Containers.SingleOrDefault(
                value => value.ContainerType == InventoryContainerType.Equipment)
            ?? throw new InvalidDataException(
                "Entry inventory has no equipment container to bind the character to.");

        // And the avatar list, for the same reason and after the same symptom. A player dragging a
        // costume inside the avatar bag was answered `ListNotServed` on 2026-09-07 -- the identical
        // refusal, from the identical cause, a fortnight after the note above was written about
        // equipment. Its items are shown from the entry projection whether or not it is bound, so
        // nothing looked wrong until something was dragged.
        var avatar = inventory.Containers.SingleOrDefault(
                value => value.ContainerType == InventoryContainerType.Avatar)
            ?? throw new InvalidDataException(
                "Entry inventory has no avatar container to bind the character to.");

        containers.Clear(characterId);
        foreach (var container in ((InventoryContainerState[])[main, equipment, avatar])
            .Concat(inventory.Containers.Where(value => value.ContainerType == InventoryContainerType.Pet)))
        {
            containers.Register(characterId, new PersistentItemContainer(
                ContainerId.ForCharacter(characterId, container.ContainerType),
                characterId,
                new ContainerShape(container.ContainerType, container.Parameter),
                itemStore,
                transactions,
                mutations,
                placementRules,
                arrivals,
                slotRules,
                slotMappings ?? new InventorySlotMappingRuleService()));
        }
    }

    public async ValueTask<EntryInventoryPreparation> ApplyDailyRefillAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken)
    {
        if (result.Character.Identity.CharacterId != session.CharacterId
            || result.Account.AccountId != session.AccountId)
            throw new InvalidDataException("Entry inventory owner does not match selected session.");

        var rules = await lifecycleRules.DailyRefillsAsync(result.World.PreparedAt, cancellationToken)
            .ConfigureAwait(false);
        var committed = await mutations.ApplyDailyRefillAsync(
                session.AccountId, session.CharacterId, rules, result.World.PreparedAt, cancellationToken)
            .ConfigureAwait(false);
        if (committed.Grants.Count == 0)
            return new EntryInventoryPreparation(result, []);

        var reloaded = await inventories.LoadAsync(
            session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        var updated = result with { CharacterInventory = reloaded };
        var updates = committed.Grants
            .GroupBy(value => value.ContainerType)
            .OrderBy(group => (byte)group.Key)
            .Select(group => new CommittedInventoryUpdateState(
                group.Key,
                ResolveCommittedItems(reloaded, group.Key, group.Select(value => value.SlotIndex)),
                result.World.PreparedAt))
            .ToArray();
        return new EntryInventoryPreparation(updated, updates);
    }

    private static IReadOnlyList<InventoryItemState> ResolveCommittedItems(
        CharacterInventoryEntryState inventory,
        InventoryContainerType containerType,
        IEnumerable<int> slots)
    {
        var container = inventory.Containers.SingleOrDefault(value => value.ContainerType == containerType)
            ?? throw new InvalidDataException($"Committed inventory container {containerType} was not reloaded.");
        return slots.Distinct().Order().Select(slot =>
            container.Items.SingleOrDefault(item => item.SlotIndex == slot)
            ?? throw new InvalidDataException($"Committed inventory slot {containerType}/{slot} was not reloaded."))
            .ToArray();
    }
}
