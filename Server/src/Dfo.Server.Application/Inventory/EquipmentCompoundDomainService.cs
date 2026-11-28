using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentCompoundDomainService(
    ISelectedSessionDomainService sessions,
    ITownAreaDomainService towns,
    IEquipmentDomainService equipment,
    ICharacterBusinessInventoryDomainService inventory,
    IBusinessInventorySlotDomainService slots,
    EquipmentCompoundRuleService rules,
    IOpeningEffectPlanRuleService materials,
    IPackageOpeningRuleService rewards,
    IApplyEffectsDomainService effects,
    TimeProvider clock) : IEquipmentCompoundDomainService
{
    private static readonly BusinessInventorySpace[] MaterialSpaces =
        [BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable, BusinessInventorySpace.Material,
            BusinessInventorySpace.ExpertMaterial, BusinessInventorySpace.SharedMaterial];

    public async ValueTask<EquipmentCompoundReceipt> CompoundAsync(SelectedSessionIdentity session, BusinessSlot source,
        bool directed, int part, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        if (!sessions.IsInWorld(session) || towns.Find(session.CharacterId)?.Session != session)
            throw new OpeningRejectedException(OpeningRejection.NoSession, "Equipment compound requires an active town session.");
        if (source.Space != BusinessInventorySpace.CarriedEquipment)
            throw new OpeningRejectedException(OpeningRejection.InvalidRequest, "Invalid equipment compound input.");
        var instance = await equipment.Of(session.CharacterId).FindAtAsync(source, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (instance is null)
            throw new OpeningRejectedException(OpeningRejection.WrongItem, "Compound equipment is missing.");
        var target = await instance.ReadAsync(ct).ConfigureAwait(false);
        if (target.Snapshot.EquipmentLockId != 0 || target.Snapshot.Lifetime.IsExpired(now))
            throw new OpeningRejectedException(OpeningRejection.WrongItem, "Compound equipment is locked or expired.");

        var plan = rules.Plan(target.ItemId, directed, part);
        var store = inventory.GetStore(session.CharacterId, BusinessInventoryStoreKind.Item);
        var held = new List<OpeningHeldItem>();
        foreach (var space in MaterialSpaces)
        {
            var bag = store.FindContainer(space);
            if (bag is null) continue;
            foreach (var item in await bag.GetAllAsync(ct).ConfigureAwait(false))
                held.Add(new(new(space, item.SlotIndex), item));
        }
        var costs = materials.MaterialCosts(plan.Materials, 1, held, now);
        var resultId = EquipmentCompoundRuleService.Draw(plan, Random.Shared.Next());
        var prepared = await rewards.PrepareAsync([new(resultId, 1)], now, ct).ConfigureAwait(false);
        if (prepared.Length != 1 || prepared[0].Item.PremiumType != 0
            || prepared[0].Item.Placement is not { WorldClass: ItemWorldClass.Equipment, Destination.Space: BusinessInventorySpace.CarriedEquipment })
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Compound result is not ordinary equipment.");
        var grant = prepared[0] with { Item = prepared[0].Item with { Sealed = target.Snapshot.SealFlag != 0 } };
        var planEffects = materials.MaterialCraftEffects(costs, grant).Insert(0, new DestroyEquipmentEffect(new(target, target.ItemId)));
        var applied = await effects.ApplyAsync(new(session.AccountId, session.CharacterId, now, footprint, session),
            planEffects, ct).ConfigureAwait(false);
        var result = applied.Changes<PreparedItemGrantChange>().Single().Slots.Single();
        var changes = await EquipmentEffectReceiptProjection.FinalSlotsAsync(session.CharacterId, applied, slots, ct).ConfigureAwait(false);
        return new(session, source, result, changes, now);
    }
}
