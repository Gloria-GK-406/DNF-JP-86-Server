using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Persistence.Tests.Support;

// Command fixtures deliberately replace inventory outcomes. Overflow persistence is tested
// separately with the production ItemDeliveryDomainService and SQLite, never with this adapter.
internal sealed class BagRewardDelivery(IItemGrantDomainService grants, ICharacterContainerDomainService containers)
    : IItemDeliveryDomainService
{
    public ValueTask<long> PendingQuantityAsync(CharacterId character, int itemId, CancellationToken ct) => ValueTask.FromResult(0L);
    public async ValueTask<ItemDeliveryReceipt> DeliverAsync(AccountId account, CharacterId character,
        IReadOnlyList<ItemDeliveryGrant> items, string source, CancellationToken ct,
        ItemDeliveryMode mode, ItemDeliveryOptions? options = null)
    {
        var added = new List<ItemDeliveryAddition>();
        var mailed = 0;
        foreach (var item in items)
        {
            if (item.PlacementConstraint is { } constraint)
            {
                // Refill command fixtures exercise restricted band selection with real containers.
                // They do not prove mail persistence; ItemDeliveryTests covers that with full DI.
                var space = constraint.Destination.Space;
                var bag = new Dfo.Server.Application.Inventory.CharacterBusinessInventoryDomainService(containers,
                    new RefillPlacementRule(constraint), transactions: TestRequired.TransactionScope(), arrivals: null!, creatures: null!, items: null!, slotRules: null!, capacities: null!, lifecycle: null!).GetStore(character, BusinessInventoryOwnership.StoreOf(space)).GetContainer(space);
                var remaining = item.Count;
                while (remaining > 0)
                {
                    var existing = await bag.GetAllAsync(ct);
                    var arrival = new Dfo.Server.Application.Inventory.ItemArrivalRuleService().ForDailyRefill(
                        existing.Where(x=>x.ItemId==item.ItemId).Select(x=>new ItemPile(new(space,x.SlotIndex),x.Count)).ToArray(),
                        remaining,constraint.Destination,existing.Select(x=>new BusinessSlot(space,x.SlotIndex)).ToHashSet(),constraint.StackLimit);
                    var next = arrival switch
                    {
                        ItemArrival.OntoExistingPile p => (p.Slot,p.Added,p.Total),
                        ItemArrival.IntoEmptySlot e => (e.Slot,e.Count,e.Count),
                        _ => ((BusinessSlot Slot,int Added,int Total)?)null,
                    };
                    if(next is null) { mailed+=remaining; break; }
                    var previous=await bag.GetAsync(next.Value.Slot.Index,ct);
                    var snapshot=previous?.Snapshot ?? new ItemSnapshot {ItemId=item.ItemId,IsPile=true,LegacyKind=2,Type=ItemTraits.Consumable};
                    var placed=await grants.ApplyPlannedAsync(bag,next.Value.Slot.Index,snapshot with {Count=next.Value.Total},ct);
                    added.Add(new(bag,next.Value.Slot,next.Value.Added,placed.Snapshot));remaining-=next.Value.Added;
                }
                continue;
            }
            var result = await grants.GrantAsync(character, containers, item.ItemId, item.Count, ct);
            var target = result.Placement switch
            {
                ItemPlacementOutcome.PutInEmptySlot x => (x.SlotIndex, x.Count),
                ItemPlacementOutcome.AddedToPile x => (x.SlotIndex, x.Added),
                ItemPlacementOutcome.NoRoom when mode != ItemDeliveryMode.InventoryOnly => (-1, 0),
                _ => throw new InvalidDataException("Unexpected fixture placement."),
            };
            if (target.Item1 < 0) { mailed += item.Count; continue; }
            var held = await result.Container.GetAsync(target.Item1, ct);
            if (held is not null && item.Durability is { } durability && result.Placement is ItemPlacementOutcome.PutInEmptySlot)
                held = await result.Container.UpdateAsync(held, x => x with { Durability = durability }, ct);
            added.Add(new(result.Container, new(result.Container.Space, target.Item1), target.Item2,
                held?.Snapshot ?? new ItemSnapshot { ItemId = item.ItemId, Count = item.Count }));
        }
        return new(added, mailed > 0 ? [1] : [], mailed);
    }

    private sealed class RefillPlacementRule(ItemDeliveryPlacementConstraint constraint) : IItemPlacementRuleService
    {
        public ValueTask<ItemPlacement?> PlacementForAsync(int id,CancellationToken ct)=>ValueTask.FromResult<ItemPlacement?>(new(2,constraint.StackLimit,constraint.Destination));
        public ValueTask<ItemWorldClass> ClassifyAsync(int id,CancellationToken ct)=>ValueTask.FromResult(ItemWorldClass.Interchangeable);
        public ValueTask<BusinessSlot?> WornSlotForAsync(int id,CancellationToken ct)=>ValueTask.FromResult<BusinessSlot?>(null);
        public ValueTask<bool> IsEquipmentAsync(int id,CancellationToken ct)=>ValueTask.FromResult(false);
    }
}
