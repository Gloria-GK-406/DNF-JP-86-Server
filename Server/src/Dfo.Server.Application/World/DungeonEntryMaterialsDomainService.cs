using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Application.Inventory;

namespace Dfo.Server.Application.World;

internal sealed class DungeonEntryMaterialsDomainService(ICharacterBusinessInventoryDomainService inventory,
    IMaterialConsumptionRuleService rules, IItemLifecycleDomainService lifecycle,
    IDomainTransactionAccessor transactions) : IDungeonEntryMaterialsDomainService
{
    public async ValueTask<DungeonEntryMaterialPreparation> PrepareAsync(IReadOnlyList<DungeonRunParticipant> participants,
        IReadOnlyList<DungeonRequiredItem> requirements, CancellationToken token)
    {
        var debits = new List<Debit>();
        if (requirements.Any(x => x.ItemId <= 0 || x.Count <= 0))
            throw new InvalidDataException("Invalid dungeon entry material requirement.");
        foreach (var member in participants)
        {
            var store = inventory.GetStore(member.CharacterId, BusinessInventoryStoreKind.Item);
            var carried = new List<(IBusinessItemContainer Container, ItemReference Item)>();
            foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable,
                         BusinessInventorySpace.Material, BusinessInventorySpace.Quest, BusinessInventorySpace.ExpertMaterial })
                if (store.FindContainer(space) is { } container)
                    carried.AddRange((await container.GetAllAsync(token).ConfigureAwait(false)).Select(x => (container, x)));
            var shared = inventory.GetPersistentStore(new ContainerOwner(InventoryOwnerType.Account, member.AccountId.Value),
                BusinessInventoryStoreKind.Item).GetContainer(BusinessInventorySpace.SharedMaterial);
            carried.AddRange((await shared.GetAllAsync(token).ConfigureAwait(false)).Select(x => (shared, x)));
            foreach (var group in requirements.GroupBy(x => x.ItemId))
            {
                var needed = group.Sum(x => (long)x.Count);
                var candidates = carried.Where(x => rules.IsConsumable(x.Item, group.Key))
                    .OrderBy(x => x.Container.Space).ThenBy(x => x.Item.SlotIndex).ToArray();
                if (candidates.Sum(x => (long)x.Item.Count) < needed) return new(null, member.Slot);
                var consume = group.Where(x => x.Consume).Sum(x => (long)x.Count);
                foreach (var candidate in candidates)
                {
                    if (consume == 0) break;
                    var count = (int)Math.Min(consume, candidate.Item.Count);
                    if (count > 0) debits.Add(new(member, candidate.Container, candidate.Item, count));
                    consume -= count;
                }
            }
        }
        return new(new Plan(debits, lifecycle, transactions));
    }

    private sealed record Debit(DungeonRunParticipant Member, IBusinessItemContainer Container, ItemReference Item, int Count);
    private sealed class Plan(IReadOnlyList<Debit> debits, IItemLifecycleDomainService lifecycle,
        IDomainTransactionAccessor transactions) : IDungeonEntryMaterialPlan
    {
        private bool consumed;
        public async ValueTask<IReadOnlyList<DungeonEntryInventoryChange>> ConsumeAsync(CancellationToken token)
        {
            if (transactions.Current is null) throw new InvalidOperationException("Entry materials require the entry transaction.");
            if (consumed) throw new InvalidOperationException("Entry material plan has already been consumed.");
            consumed = true;
            var changes = new List<DungeonEntryInventoryChange>();
            foreach (var debit in debits)
            {
                var remaining = await ItemContainerOperations.ConsumeAsync(debit.Container, debit.Item, debit.Count, lifecycle, token).ConfigureAwait(false);
                changes.Add(new(debit.Member.CharacterId, new(debit.Container.Space, debit.Item.SlotIndex),
                    remaining == 0 ? null : (debit.Item.Snapshot with { Count = remaining }).ToCore()));
            }
            return changes;
        }
    }
}
