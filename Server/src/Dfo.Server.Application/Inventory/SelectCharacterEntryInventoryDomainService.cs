using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class SelectCharacterEntryInventoryDomainService(
    IEntryLifecycleContentSource content,
    IEntryInventoryMutationRepository mutations,
    ICharacterInventoryEntryDomainService inventories,
    IEntryInventoryLeasePersistence persistence,
    IEntryInventoryLeaseRegistry leases,
    ICharacterContainerDomainService containers,
    IItemStore itemStore,
    IDomainTransactionAccessor transactions,
    IItemPlacementDefinitionSource placements,
    IItemArrivalRuleService arrivals,
    IItemSlotRuleService slotRules) : ISelectCharacterEntryInventoryDomainService
{
    public async ValueTask SaveExistingLeaseAsync(
        SelectedSessionIdentity previousSession, CancellationToken cancellationToken)
    {
        if (leases.Find(previousSession.AccountId) is { } current
            && current.Session == previousSession && current.IsDirty)
            await persistence.SaveAsync(current, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SelectCharacterResult> PrepareLeaseAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken)
    {
        var reloaded = await inventories.LoadAsync(
            session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
        leases.Replace(session, reloaded);
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

        containers.Clear(characterId);
        containers.Register(characterId, new PersistentItemContainer(
            ContainerId.ForCharacter(characterId, main.ContainerType),
            characterId,
            new ContainerShape(main.ContainerType, main.Parameter),
            itemStore,
            transactions,
            mutations,
            placements,
            arrivals,
            slotRules));
    }

    public async ValueTask<EntryInventoryPreparation> ApplyDailyRefillAsync(
        SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken)
    {
        if (result.Character.Identity.CharacterId != session.CharacterId
            || result.Account.AccountId != session.AccountId)
            throw new InvalidDataException("Entry inventory owner does not match selected session.");

        try
        {
            var rules = await content.GetDailyRefillRulesAsync(result.World.PreparedAt, cancellationToken)
                .ConfigureAwait(false);
            var committed = await mutations.ApplyDailyRefillAsync(
                    session.AccountId, session.CharacterId, rules, result.World.PreparedAt, cancellationToken)
                .ConfigureAwait(false);
            if (committed.Grants.Count == 0)
                return new EntryInventoryPreparation(result, []);

            var reloaded = await inventories.LoadAsync(
                session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false);
            var updated = result with { CharacterInventory = reloaded };
            leases.Replace(session, reloaded);
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
        catch
        {
            // The repository transaction owns rollback. Reload restores a trustworthy
            // lease before the workflow compensates the selected session.
            var restored = await inventories.LoadAsync(
                session.AccountId, session.CharacterId, CancellationToken.None).ConfigureAwait(false);
            leases.Replace(session, restored);
            throw;
        }
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

internal sealed class EntryInventoryLeaseRegistry : IEntryInventoryLeaseRegistry
{
    private readonly object sync = new();
    private readonly Dictionary<Dfo.Server.Data.Accounts.AccountId, EntryInventoryLeaseSnapshot> leases = [];
    private long nextVersion;

    public EntryInventoryLeaseSnapshot? Find(Dfo.Server.Data.Accounts.AccountId accountId)
    {
        lock (sync) return leases.GetValueOrDefault(accountId);
    }

    public bool TryMarkDirtyExact(SelectedSessionIdentity session, CharacterItemLeaseState inventory)
    {
        lock (sync)
        {
            if (!leases.TryGetValue(session.AccountId, out var lease) || lease.Session != session)
                return false;
            leases[session.AccountId] = lease with { Inventory = inventory, IsDirty = true };
            return true;
        }
    }

    public void Replace(SelectedSessionIdentity session, CharacterInventoryEntryState inventory)
    {
        lock (sync)
        {
            var version = checked(++nextVersion);
            leases[session.AccountId] = new EntryInventoryLeaseSnapshot(
                session, new CharacterItemLeaseState(inventory.CharacterId, inventory.Containers), version, false);
        }
    }

    public bool ReleaseExact(SelectedSessionIdentity session)
    {
        lock (sync)
            return leases.TryGetValue(session.AccountId, out var lease)
                && lease.Session == session
                && leases.Remove(session.AccountId);
    }

}
