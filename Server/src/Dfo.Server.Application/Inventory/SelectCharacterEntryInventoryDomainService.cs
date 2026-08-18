using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class SelectCharacterEntryInventoryDomainService(
    IEntryLifecycleContentSource content,
    IEntryInventoryMutationRepository mutations,
    ICharacterInventoryEntryDomainService inventories,
    IEntryInventoryLeasePersistence persistence,
    IEntryInventoryLeaseRegistry leases) : ISelectCharacterEntryInventoryDomainService
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
        return result with { CharacterInventory = reloaded };
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
