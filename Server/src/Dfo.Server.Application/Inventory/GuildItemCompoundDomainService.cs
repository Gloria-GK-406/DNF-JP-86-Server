using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class GuildItemCompoundDomainService(GuildItemCompoundRuleService rules,
    IBusinessInventorySlotDomainService slots, IItemLifecycleDomainService lifecycle,
    IItemDeliveryDomainService delivery, IGuildHallAccessDomainService halls,
    TimeProvider clock)
    : IGuildItemCompoundDomainService
{
    public async ValueTask<GuildCompoundReceipt?> CompoundAsync(SelectedSessionIdentity session, GuildCompoundInput input,
        IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        if (await halls.VisitAsync(session, GuildHallTown.DungeonGateAreaId, ct).ConfigureAwait(false) is null
            || rules.Recipe(input) is not { } rule)
            return null;

        var gem = input.Kind == GuildCompoundKind.GuardianGem;
        var space = gem ? BusinessInventorySpace.GuardianGem : BusinessInventorySpace.GuildMedal;
        var costs = new List<Material>();
        var now = clock.GetUtcNow();
        foreach (var group in input.Materials.GroupBy(slot => slot))
        {
            var address = group.Key;
            var count = group.Count();
            if (address.Space != space || address.Index is < 0 or >= 49 || !gem && count != 1
                || slots.Find(session.CharacterId, address) is not { } bound
                || await bound.Container.GetAsync(bound.Slot.Index, ct).ConfigureAwait(false) is not { } item
                || item.IsPile != gem || item.Count < count
                || item.Snapshot.LegacyKind != (gem ? 13 : 12)
                || item.Snapshot.IsMirror || item.Snapshot.EquipmentLockId != 0 || item.Snapshot.SortLockFlag != 0
                || item.Snapshot.Lifetime.IsExpired(now)
                || !rules.AcceptsMaterial(input, item.ItemId))
                return null;
            costs.Add(new(address, bound.Container, item, count));
        }

        var product = rules.DrawProduct(rule, input.Materials.Count);
        foreach (var cost in costs)
            await ItemContainerOperations.ConsumeAsync(cost.Container, cost.Item, cost.Count, lifecycle, ct)
                .ConfigureAwait(false);

        var reward = await delivery.DeliverAsync(session.AccountId, session.CharacterId, [new(product, 1)], "公会合成",
            ct, ItemDeliveryMode.InventoryOnly).ConfigureAwait(false);
        var output = reward.Inventory.Single();
        var replacesConsumedSlot = costs.Any(cost => cost.Slot == output.Slot && cost.Count == cost.Item.Count);
        var changes = new List<GuildCompoundSlotChange> { new(output.Slot, output.Snapshot, replacesConsumedSlot) };
        foreach (var cost in costs)
        {
            if (cost.Slot == output.Slot) continue;
            var remaining = await cost.Container.GetAsync(cost.Slot.Index, ct).ConfigureAwait(false);
            changes.Add(new(cost.Slot, remaining?.Snapshot));
        }
        footprint.RecordInventorySlots(session, changes.Select(change => change.Slot).ToArray());
        return new(product, changes);
    }

    private sealed record Material(BusinessSlot Slot, IBusinessItemContainer Container, ItemReference Item, int Count);
}
