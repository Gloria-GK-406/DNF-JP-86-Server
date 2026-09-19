using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentDisjointDomainService(
    ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers,
    IBusinessInventorySlotDomainService slots,
    IEquipmentDisjointRuleService rules,
    IItemPlacementRuleService placements,
    IItemSlotRuleService slotRules,
    IItemArrivalRuleService arrivals,
    IInventorySlotMappingRuleService? slotMappings = null) : IEquipmentDisjointDomainService
{
    private readonly IInventorySlotMappingRuleService slotMappings =
        slotMappings ?? new InventorySlotMappingRuleService();
    public async ValueTask<EquipmentDisjointResult> DisjointAsync(
        SelectedSessionIdentity session,
        BusinessSlot targetSlot,
        BusinessSlot? disjointToolSlot,
        EquipmentDisjointSettings settings,
        CancellationToken cancellationToken)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId
            || online.CurrentCharacterId != session.CharacterId)
            return new(EquipmentDisjointRejection.NoSession);
        // A21 reports the disjoint provider id in the protocol layer, but that client value is not
        // domain authority. Portable-tool eligibility is represented only by its mapped business
        // slot; the current system-NPC method intentionally accepts no tool.
        if (!targetSlot.Space.IsCarriedItemSpace()
            || disjointToolSlot is not null
            || settings.Method != EquipmentDisjointMethod.SystemNpc)
            return new(EquipmentDisjointRejection.InvalidRequest);

        var bound = slots.Find(session.CharacterId, targetSlot);
        var equipment = bound is null
            ? null
            : await bound.Container.ReadSlotAsync(bound.StorageIndex, cancellationToken).ConfigureAwait(false);
        if (equipment is null)
            return new(EquipmentDisjointRejection.ItemUnavailable);
        var placement = await placements.PlacementForAsync(equipment.ItemId, cancellationToken)
            .ConfigureAwait(false);
        if (placement is null)
            return new(EquipmentDisjointRejection.CannotDisjoint);
        if (!slotRules.CanOccupySlot(targetSlot, placement))
            throw new InvalidDataException(
                $"Stored item {equipment.ItemId} contradicts business slot "
                    + $"{targetSlot.Space}/{targetSlot.Index}.");
        var instance = EquipmentInstance.From(equipment);
        if (instance is null)
            return new(EquipmentDisjointRejection.CannotDisjoint);

        var decided = await rules.ResolveAsync(
            instance.DisjointCandidate(), settings, Random.Shared.Next(), cancellationToken)
            .ConfigureAwait(false);
        if (decided.Rejection != EquipmentDisjointRejection.None
            || decided.Rewards is not { Count: > 0 } rewards)
            return new(decided.Rejection == EquipmentDisjointRejection.None
                ? EquipmentDisjointRejection.CannotDisjoint : decided.Rejection);

        var planned = await PlanAsync(
            bound!, rewards, cancellationToken).ConfigureAwait(false);
        if (planned is null)
            return new(EquipmentDisjointRejection.InventoryFull);
        if (planned.Materials.Count > byte.MaxValue)
            return new(EquipmentDisjointRejection.CannotDisjoint);

        await bound!.Container.ClearAsync(bound.StorageIndex, cancellationToken).ConfigureAwait(false);
        foreach (var write in planned.Writes.OrderBy(entry => entry.Key))
            await bound.Container.PutAsync(write.Key, write.Value, cancellationToken).ConfigureAwait(false);
        return new(EquipmentDisjointRejection.None,
            new(session, targetSlot, planned.Materials));
    }

    private async ValueTask<DisjointPlan?> PlanAsync(
        BoundInventorySlot source,
        IReadOnlyList<EquipmentDisjointReward> rewards,
        CancellationToken cancellationToken)
    {
        var standing = (await source.Container.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(item => item.SlotIndex);
        standing.Remove(source.StorageIndex);
        var writes = new Dictionary<int, InventoryItemCoreState>();
        var materials = new List<EquipmentDisjointMaterialReceipt>();
        foreach (var reward in rewards)
        {
            if (reward.ItemId <= 0 || reward.Count <= 0)
                return null;
            var placement = await placements.PlacementForAsync(reward.ItemId, cancellationToken)
                .ConfigureAwait(false);
            if (placement is not { WorldClass: ItemWorldClass.Interchangeable, ItemKind: > 0 })
                return null;
            var remaining = reward.Count;
            while (remaining > 0)
            {
                var candidates = CandidateSlots(placement).ToArray();
                var arrival = arrivals.ForGrant(
                    candidates.Select(slot =>
                            (Slot: slot, Index: this.StorageIndex(source.Container, slot)))
                        .Where(candidate => standing.TryGetValue(candidate.Index, out var item)
                            && item.IsPile && item.ItemId == reward.ItemId)
                        .Select(candidate => new ItemPile(
                            candidate.Slot, standing[candidate.Index].Count))
                        .ToArray(),
                    remaining,
                    placement,
                    candidates.Where(slot =>
                            standing.ContainsKey(this.StorageIndex(source.Container, slot)))
                        .ToHashSet());
                int slot;
                int added;
                BusinessSlot materialSlot;
                InventoryItemCoreState core;
                switch (arrival)
                {
                    case ItemArrival.OntoExistingPile pile:
                        materialSlot = pile.Slot;
                        slot = this.StorageIndex(source.Container, pile.Slot);
                        added = pile.Added;
                        core = standing[slot].Core with { Value = pile.Total };
                        break;
                    case ItemArrival.IntoEmptySlot empty:
                        materialSlot = empty.Slot;
                        slot = this.StorageIndex(source.Container, empty.Slot);
                        added = empty.Count;
                        core = StackableItemCore.Create(placement.ItemKind, reward.ItemId, added);
                        break;
                    case ItemArrival.NoRoom:
                        return null;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(arrival), arrival, "Unknown item arrival decision.");
                }
                if (slot > short.MaxValue || added <= 0 || added > remaining)
                    return null;
                standing[slot] = new(slot, core, ItemWorldClass.Interchangeable);
                writes[slot] = core;
                materials.Add(new(materialSlot, reward.ItemId, added));
                remaining -= added;
            }
        }
        return new(writes, materials);
    }

    private int StorageIndex(IItemContainer container, BusinessSlot slot)
    {
        var game = slotMappings.ToGameSlot(slot);
        if (game.Space != container.Type)
            throw new InvalidDataException(
                $"Disjoint reward slot {slot.Space}/{slot.Index} does not belong to {container.Type}.");
        return game.Index;
    }

    private static IEnumerable<BusinessSlot> CandidateSlots(ItemPlacement placement)
    {
        var own = Enumerable.Range(
                placement.Destination.FirstIndex,
                placement.Destination.LastIndex - placement.Destination.FirstIndex + 1)
            .Select(index => new BusinessSlot(placement.Destination.Space, index));
        return ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind)
            ? own.Concat(Enumerable.Range(0, 6)
                .Select(index => new BusinessSlot(BusinessInventorySpace.QuickSlot, index)))
            : own;
    }

    private sealed record DisjointPlan(
        IReadOnlyDictionary<int, InventoryItemCoreState> Writes,
        IReadOnlyList<EquipmentDisjointMaterialReceipt> Materials);
}
