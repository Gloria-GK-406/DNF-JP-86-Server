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
    IEquipmentDomainService equipments,
    IEquipmentDisjointRuleService rules,
    IEquipmentDisjointDefinitionSource definitions,
    IItemPlacementRuleService placements,
    IItemSlotRuleService slotRules,
    IItemArrivalRuleService arrivals,
    IAccountMaterialBagDomainService accountMaterials,
    IAccountMaterialRuleService accountMaterialRules,
    TimeProvider timeProvider) : IEquipmentDisjointDomainService
{

    public ValueTask<EquipmentDisjointResult> DisjointAtNpcAsync(
        SelectedSessionIdentity session,
        BusinessSlot targetSlot,
        CancellationToken cancellationToken) =>
        this.DisjointAsync(session, targetSlot, null, cancellationToken);

    public ValueTask<EquipmentDisjointResult> DisjointWithPortableMachineAsync(
        SelectedSessionIdentity session,
        BusinessSlot targetSlot,
        BusinessSlot disjointToolSlot,
        CancellationToken cancellationToken) =>
        this.DisjointAsync(session, targetSlot, new(disjointToolSlot), cancellationToken);

    private async ValueTask<EquipmentDisjointResult> DisjointAsync(
        SelectedSessionIdentity session,
        BusinessSlot targetSlot,
        PortableMachineUse? portableMachine,
        CancellationToken cancellationToken)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId
            || online.CurrentCharacterId != session.CharacterId)
            return new(EquipmentDisjointRejection.NoSession);
        if (!targetSlot.Space.IsCarriedItemSpace()
            || portableMachine is { Slot: var toolSlot }
                && (!toolSlot.Space.IsCarriedItemSpace() || toolSlot == targetSlot))
            return new(EquipmentDisjointRejection.InvalidRequest);

        BoundInventorySlot? toolBound = null;
        ContainerSlotItem? tool = null;
        PortableDisjointMachineDefinition? portableDefinition = null;
        if (portableMachine is { Slot: var portableSlot })
        {
            toolBound = slots.Find(session.CharacterId, portableSlot);
            tool = toolBound is null
                ? null
                : await toolBound.Container.ReadSlotAsync(
                    toolBound.Slot.Index, cancellationToken).ConfigureAwait(false);
            if (tool is not { IsPile: true, Count: > 0 })
                return new(EquipmentDisjointRejection.ItemUnavailable);
            var toolPlacement = await placements.PlacementForAsync(tool.ItemId, cancellationToken)
                .ConfigureAwait(false);
            if (toolPlacement is null)
                return new(EquipmentDisjointRejection.InvalidRequest);
            if (!slotRules.CanOccupySlot(portableSlot, toolPlacement))
                throw new InvalidDataException(
                    $"Stored item {tool.ItemId} contradicts business slot "
                        + $"{portableSlot.Space}/{portableSlot.Index}.");
            portableDefinition = await definitions.GetPortableMachineAsync(
                tool.ItemId, cancellationToken).ConfigureAwait(false);
            if (portableDefinition is null)
                return new(EquipmentDisjointRejection.UnsupportedMethod);
        }

        var bound = slots.Find(session.CharacterId, targetSlot);
        var equipment = bound is null
            ? null
            : await bound.Container.ReadSlotAsync(bound.Slot.Index, cancellationToken).ConfigureAwait(false);
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
        var instance = await equipments.Of(session.CharacterId).FindAtAsync(targetSlot, cancellationToken).ConfigureAwait(false);
        if (instance is null)
            return new(EquipmentDisjointRejection.CannotDisjoint);

        if (portableDefinition is not null)
        {
            var equipmentDefinition = await definitions.GetEquipmentAsync(
                equipment.ItemId, cancellationToken).ConfigureAwait(false);
            if (equipmentDefinition is null
                || equipmentDefinition.MinimumLevel > portableDefinition.MaximumEquipmentLevel)
                return new(EquipmentDisjointRejection.CannotDisjoint);
        }

        var facts = await instance.ReadAsync(cancellationToken).ConfigureAwait(false);
        var decided = await rules.ResolveAsync(
            new(facts.Core.ItemId, facts.Core), EquipmentDisjointSettings.System,
            Random.Shared.Next(), cancellationToken)
            .ConfigureAwait(false);
        if (decided.Rejection != EquipmentDisjointRejection.None
            || decided.Rewards is not { Count: > 0 } rewards)
            return new(decided.Rejection == EquipmentDisjointRejection.None
                ? EquipmentDisjointRejection.CannotDisjoint : decided.Rejection);

        var planned = await PlanAsync(
            session.CharacterId, bound!, rewards, cancellationToken).ConfigureAwait(false);
        if (planned is null)
            return new(EquipmentDisjointRejection.InventoryFull);
        if (planned.Materials.Count + planned.AccountAdditions.Count > byte.MaxValue)
            return new(EquipmentDisjointRejection.CannotDisjoint);

        InventoryItemCoreState? toolFinalCore = null;
        if (toolBound is not null && tool is not null)
        {
            if (tool.Count == 1)
                await toolBound.Container.ClearAsync(
                    toolBound.Slot.Index, cancellationToken).ConfigureAwait(false);
            else
            {
                toolFinalCore = tool.Core with { Value = tool.Count - 1 };
                await toolBound.Container.PutAsync(
                    toolBound.Slot.Index,
                    toolFinalCore,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        await instance.DestroyAsync(cancellationToken).ConfigureAwait(false);
        foreach (var write in planned.Writes.OrderBy(entry => entry.Key.Space).ThenBy(entry => entry.Key.Index))
        {
            var target = slots.Get(session.CharacterId, write.Key);
            await target.Container.PutAsync(write.Key.Index, write.Value, cancellationToken).ConfigureAwait(false);
        }
        var accountChanges = planned.AccountAdditions.Count == 0
            ? []
            : await accountMaterials.Get(session.AccountId)
                .AddAsync(planned.AccountAdditions, cancellationToken)
                .ConfigureAwait(false);

        var changes = new List<EquipmentDisjointSlotChange>
        {
            new(targetSlot, null),
        };
        if (portableMachine is not null)
            changes.Add(new(portableMachine.Slot, toolFinalCore));
        changes.AddRange(planned.FinalSlots
            .OrderBy(entry => entry.Key.Space)
            .ThenBy(entry => entry.Key.Index)
            .Select(entry => new EquipmentDisjointSlotChange(entry.Key, entry.Value)));

        return new(EquipmentDisjointRejection.None,
            new(session, targetSlot, planned.Materials, accountChanges, changes,
                timeProvider.GetUtcNow()));
    }

    private async ValueTask<DisjointPlan?> PlanAsync(
        Dfo.Server.Data.Characters.CharacterId characterId,
        BoundInventorySlot source,
        IReadOnlyList<EquipmentDisjointReward> rewards,
        CancellationToken cancellationToken)
    {
        var standing = new Dictionary<BusinessSlot, ContainerSlotItem>();
        var loaded = new HashSet<BusinessInventorySpace>();
        async ValueTask<bool> LoadAsync(BusinessInventorySpace space)
        {
            if (!loaded.Add(space)) return true;
            var container = slots.Find(characterId, new BusinessSlot(space, 0))?.Container;
            if (container is null) return false;
            foreach (var item in await container.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                standing.Add(new BusinessSlot(space, item.SlotIndex), item);
            return true;
        }
        if (!await LoadAsync(source.Slot.Space).ConfigureAwait(false)) return null;
        standing.Remove(source.Slot);
        var writes = new Dictionary<BusinessSlot, InventoryItemCoreState>();
        var materials = new List<EquipmentDisjointMaterialReceipt>();
        var finalSlots = new Dictionary<BusinessSlot, InventoryItemCoreState>();
        var accountAdditions = new Dictionary<AccountMaterialKind, int>();
        foreach (var reward in rewards)
        {
            if (reward.ItemId <= 0 || reward.Count <= 0)
                return null;
            if (accountMaterialRules.TryGetByItemId(reward.ItemId, out var accountMaterial))
            {
                accountAdditions[accountMaterial.Kind] = checked(
                    accountAdditions.GetValueOrDefault(accountMaterial.Kind) + reward.Count);
                continue;
            }
            var placement = await placements.PlacementForAsync(reward.ItemId, cancellationToken)
                .ConfigureAwait(false);
            if (placement is not { WorldClass: ItemWorldClass.Interchangeable, ItemKind: > 0 })
                return null;
            var remaining = reward.Count;
            while (remaining > 0)
            {
                var candidates = CandidateSlots(placement).ToArray();
                foreach (var space in candidates.Select(value => value.Space).Distinct())
                    if (!await LoadAsync(space).ConfigureAwait(false)) return null;
                var arrival = arrivals.ForGrant(
                    candidates.Select(slot =>
                            (Slot: slot, Item: standing.GetValueOrDefault(slot)))
                        .Where(candidate => candidate.Item is { } item
                            && item.IsPile && item.ItemId == reward.ItemId)
                        .Select(candidate => new ItemPile(
                            candidate.Slot, candidate.Item!.Count))
                        .ToArray(),
                    remaining,
                    placement,
                    candidates.Where(standing.ContainsKey).ToHashSet());
                int added;
                BusinessSlot materialSlot;
                InventoryItemCoreState core;
                switch (arrival)
                {
                    case ItemArrival.OntoExistingPile pile:
                        materialSlot = pile.Slot;
                        added = pile.Added;
                        core = standing[materialSlot].Core with { Value = pile.Total };
                        break;
                    case ItemArrival.IntoEmptySlot empty:
                        materialSlot = empty.Slot;
                        added = empty.Count;
                        core = StackableItemCore.Create(placement.ItemKind, reward.ItemId, added);
                        break;
                    case ItemArrival.NoRoom:
                        return null;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(arrival), arrival, "Unknown item arrival decision.");
                }
                if (materialSlot.Index > short.MaxValue || added <= 0 || added > remaining)
                    return null;
                standing[materialSlot] = new(materialSlot.Index, core, ItemWorldClass.Interchangeable);
                writes[materialSlot] = core;
                finalSlots[materialSlot] = core;
                materials.Add(new(materialSlot, reward.ItemId, added));
                remaining -= added;
            }
        }
        return new(writes, materials, finalSlots, accountAdditions);
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
        IReadOnlyDictionary<BusinessSlot, InventoryItemCoreState> Writes,
        IReadOnlyList<EquipmentDisjointMaterialReceipt> Materials,
        IReadOnlyDictionary<BusinessSlot, InventoryItemCoreState> FinalSlots,
        IReadOnlyDictionary<AccountMaterialKind, int> AccountAdditions);

    private sealed record PortableMachineUse(BusinessSlot Slot);
}
