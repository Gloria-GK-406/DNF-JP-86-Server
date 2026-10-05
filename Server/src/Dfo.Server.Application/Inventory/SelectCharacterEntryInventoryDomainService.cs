using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Accounts;
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
    IItemSlotRuleService slotRules,
    ICharacterEntryPreparationRuleService preparationRules,
    IInventorySlotMappingRuleService? slotMappings = null, IInventoryCapacityDomainService? capacities = null,
    ITransientItemDomainStoreFactory? transientStores = null) : ISelectCharacterEntryInventoryDomainService
{
    private readonly IInventorySlotMappingRuleService slotMappings = slotMappings ?? new InventorySlotMappingRuleService();
    public async ValueTask<SelectCharacterResult> PrepareLeaseAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken)
    {
        var reloaded = await inventories.LoadAsync(
            session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        reloaded = await SupportCreatureMarkerProjection.CompleteAsync(
            reloaded, preparationRules, result.World.PreparedAt, cancellationToken).ConfigureAwait(false);
        BindContainers(session.AccountId, session.CharacterId, reloaded, result.AccountInventory.Cargo.Container);
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
    private void BindContainers(AccountId accountId, CharacterId characterId, CharacterInventoryEntryState inventory,
        InventoryContainerState accountCargo)
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

        var transient = (transientStores ?? throw new InvalidOperationException("Memory inventory storage is not configured.")).Create();
        containers.Clear(characterId);
        foreach (var container in ((InventoryContainerState[])[main, equipment, avatar])
            .Concat(inventory.Containers.Where(value => value.ContainerType is InventoryContainerType.Pet
                or InventoryContainerType.PersonalCargo)).Append(accountCargo)
            .Append(new InventoryContainerState(InventoryContainerType.SharedMaterial, 11, [])))
        {
            containers.Register(characterId, new PersistentItemContainer(
                container.ContainerType is InventoryContainerType.AccountCargo or InventoryContainerType.SharedMaterial
                    ? ContainerId.ForAccount(accountId, container.ContainerType)
                    : ContainerId.ForCharacter(characterId, container.ContainerType),
                new ContainerShape(container.ContainerType, container.Parameter),
                itemStore,
                transactions,
                placementRules,
                slotRules, capacities));
        }
        containers.Register(characterId, new MemoryItemContainer(
            new ContainerId($"tower:empty:{characterId.Value}:{Guid.NewGuid():N}"), new(InventoryContainerType.QuickSlot, 6),
            transient, transactions, placementRules, slotRules));
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
        reloaded = await SupportCreatureMarkerProjection.CompleteAsync(
            reloaded, preparationRules, result.World.PreparedAt, cancellationToken).ConfigureAwait(false);
        var updated = result with { CharacterInventory = reloaded };
        var updates = committed.Grants
            .Select(value => slotMappings.ToGameSlot(value.Slot))
            .GroupBy(value => value.Space)
            .OrderBy(group => (byte)group.Key)
            .Select(group => new CommittedInventoryUpdateState(
                group.Key,
                ResolveCommittedItems(reloaded, group.Key, group.Select(value => value.Index)),
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
