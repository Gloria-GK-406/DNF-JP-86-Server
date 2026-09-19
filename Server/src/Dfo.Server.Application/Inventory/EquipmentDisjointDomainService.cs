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
    ICharacterContainerDomainService containers,
    IEquipmentDisjointRuleService rules,
    IItemPlacementRuleService placements,
    IItemArrivalRuleService arrivals) : IEquipmentDisjointDomainService
{
    public async ValueTask<EquipmentDisjointResult> DisjointAsync(
        SelectedSessionIdentity session,
        short targetSlotIndex,
        InventoryContainerType itemSpace,
        short disjointItemSlotIndex,
        int contextValue,
        EquipmentDisjointSettings settings,
        CancellationToken cancellationToken)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId
            || online.CurrentCharacterId != session.CharacterId)
            return new(EquipmentDisjointRejection.NoSession);
        // Portable tools use another client path. The final request dword is an opaque client/UI
        // context: A21 forwards it for ordinary system disjoint and the reference server does not
        // use it as authority. Eligibility therefore comes from the selected tool slot and rules,
        // never from this caller-provided context value.
        if (itemSpace != InventoryContainerType.Main || targetSlotIndex < 3
            || disjointItemSlotIndex != -1
            || settings.Method != EquipmentDisjointMethod.SystemNpc)
            return new(EquipmentDisjointRejection.InvalidRequest);

        var bag = containers.FindContainer(session.CharacterId, InventoryContainerType.Main);
        var equipment = bag is null
            ? null
            : await bag.ReadSlotAsync(targetSlotIndex, cancellationToken).ConfigureAwait(false);
        if (equipment is null)
            return new(EquipmentDisjointRejection.ItemUnavailable);
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
            bag!, targetSlotIndex, rewards, cancellationToken).ConfigureAwait(false);
        if (planned is null)
            return new(EquipmentDisjointRejection.InventoryFull);
        if (planned.Materials.Count > byte.MaxValue)
            return new(EquipmentDisjointRejection.CannotDisjoint);

        await bag!.ClearAsync(targetSlotIndex, cancellationToken).ConfigureAwait(false);
        foreach (var write in planned.Writes.OrderBy(entry => entry.Key))
            await bag.PutAsync(write.Key, write.Value, cancellationToken).ConfigureAwait(false);
        return new(EquipmentDisjointRejection.None,
            new(session, targetSlotIndex, itemSpace, planned.Materials));
    }

    private async ValueTask<DisjointPlan?> PlanAsync(
        IItemContainer bag,
        int sourceSlot,
        IReadOnlyList<EquipmentDisjointReward> rewards,
        CancellationToken cancellationToken)
    {
        var standing = (await bag.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(item => item.SlotIndex);
        standing.Remove(sourceSlot);
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
                var arrival = arrivals.ForGrant(
                    standing.Values
                        .Where(item => item.IsPile && item.ItemId == reward.ItemId)
                        .Select(item => new ItemPile(item.SlotIndex, item.Count))
                        .ToArray(),
                    remaining,
                    placement,
                    standing.Keys.ToHashSet());
                int slot;
                int added;
                InventoryItemCoreState core;
                switch (arrival)
                {
                    case ItemArrival.OntoExistingPile pile:
                        slot = pile.SlotIndex;
                        added = pile.Added;
                        core = standing[slot].Core with { Value = pile.Total };
                        break;
                    case ItemArrival.IntoEmptySlot empty:
                        slot = empty.SlotIndex;
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
                materials.Add(new((short)slot, reward.ItemId, added));
                remaining -= added;
            }
        }
        return new(writes, materials);
    }

    private sealed record DisjointPlan(
        IReadOnlyDictionary<int, InventoryItemCoreState> Writes,
        IReadOnlyList<EquipmentDisjointMaterialReceipt> Materials);
}
