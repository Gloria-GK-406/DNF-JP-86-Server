using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentDisjointDomainService(
    ISelectedSessionDomainService sessions,
    IBusinessInventorySlotDomainService slots,
    IEquipmentDomainService equipments,
    IEquipmentEffectPlanRuleService rules,
    IApplyEffectsDomainService effects,
    IInventoryEffectPreflightDomainService preflight,
    IItemPlacementRuleService placements,
    IItemSlotRuleService slotRules,
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
        if (!sessions.IsInWorld(session))
            return new(EquipmentDisjointRejection.NoSession);
        if (!targetSlot.Space.IsCarriedItemSpace()
            || portableMachine is { Slot: var toolSlot }
                && (!toolSlot.Space.IsCarriedItemSpace() || toolSlot == targetSlot))
            return new(EquipmentDisjointRejection.InvalidRequest);

        BoundInventorySlot? toolBound = null;
        ItemReference? tool = null;
        if (portableMachine is { Slot: var portableSlot })
        {
            toolBound = slots.Find(session.CharacterId, portableSlot);
            tool = toolBound is null
                ? null
                : await toolBound.Container.GetAsync(
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

        }

        var bound = slots.Find(session.CharacterId, targetSlot);
        var equipment = bound is null
            ? null
            : await bound.Container.GetAsync(bound.Slot.Index, cancellationToken).ConfigureAwait(false);
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

        var facts = await instance.ReadAsync(cancellationToken).ConfigureAwait(false);
        var plan = await rules.DisjointAsync(facts, portableMachine is not null
            ? new MaterialCost(portableMachine.Slot, tool!.ItemId, 1) : null,
            Random.Shared.Next(), cancellationToken).ConfigureAwait(false);
        if (plan.Decision.Rejection != EquipmentDisjointRejection.None) return new(plan.Decision.Rejection);
        var context = new EffectExecutionContext(session.AccountId, session.CharacterId, timeProvider.GetUtcNow(), session);
        var preview = await preflight.PreviewAsync(context, plan.Effects, cancellationToken).ConfigureAwait(false);
        if (preview.Rejection != InventoryEffectRejection.None) return new(preview.Rejection switch
        {
            InventoryEffectRejection.NoRoom => EquipmentDisjointRejection.InventoryFull,
            InventoryEffectRejection.UnsupportedItem => EquipmentDisjointRejection.CannotDisjoint,
            InventoryEffectRejection.InvalidTarget or InventoryEffectRejection.InsufficientAssets => EquipmentDisjointRejection.ItemUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(preview), preview.Rejection, "Unknown disjoint preview rejection."),
        });
        if (preview.RewardEntries > byte.MaxValue)
            return new(EquipmentDisjointRejection.CannotDisjoint);
        var applied = await effects.ApplyAsync(context, plan.Effects, cancellationToken).ConfigureAwait(false);
        var final = await EquipmentEffectReceiptProjection.FinalSlotsAsync(session.CharacterId, applied, slots, cancellationToken)
            .ConfigureAwait(false);
        var physical = EquipmentEffectReceiptProjection.Rewards(applied)
            .Select(reward => new EquipmentDisjointMaterialReceipt(reward.Slot, reward.ItemId, reward.Count)).ToArray();
        return new(EquipmentDisjointRejection.None, new(session, targetSlot, physical,
            final.Select(change => new EquipmentDisjointSlotChange(change.Slot, change.Core)).ToArray(),
            context.At, portableMachine?.Slot));
    }

    private sealed record PortableMachineUse(BusinessSlot Slot);
}
